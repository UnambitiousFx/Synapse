using UnambitiousFx.Functional.Failures;

namespace UnambitiousFx.Synapse.Authorization;

/// <summary>A denied check: what to report to the recorders, and what to return to the caller.</summary>
internal readonly record struct Denied(AuthorizationDenial Denial, Failure Failure);

/// <summary>
///     The authorization checks, shared by the request behaviors and the stream behavior so both pipelines apply
///     exactly the same rules. Each returns <see langword="null" /> when the request may go on.
/// </summary>
internal static class AuthorizationChecks
{
    /// <summary>Whether the actor, role and scope checks let a system principal through unchecked.</summary>
    public static bool SkipsIdentityChecks(ICurrentPrincipal principal, SystemBypass bypass)
    {
        return bypass == SystemBypass.All && principal.IsSystem;
    }

    /// <summary>Whether the request policy and resource checks let a system principal through unchecked.</summary>
    public static bool SkipsResourceChecks(ICurrentPrincipal principal, SystemBypass bypass)
    {
        // Spelled out rather than `!= None`, so an out-of-range value fails closed.
        return bypass is SystemBypass.All or SystemBypass.ResourceChecks && principal.IsSystem;
    }

    public static Denied? Actor<TRequest>(TRequest request, ICurrentPrincipal principal)
    {
        if (principal.IsAuthenticated || request is IAllowAnonymous)
        {
            return null;
        }

        return new Denied(new AuthorizationDenial(request!, AuthorizationDenialReason.Unauthenticated, principal.Id),
            new UnauthenticatedFailure("Authentication is required."));
    }

    public static Denied? Role<TRequest>(TRequest request, ICurrentPrincipal principal)
    {
        if (request is not IRequiresRole requiresRole)
        {
            return null;
        }

        var role = AuthorizationDenials.RequireValue<TRequest>(requiresRole.Role, nameof(IRequiresRole),
            nameof(IRequiresRole.Role));
        if (principal.IsInRole(role))
        {
            return null;
        }

        return new Denied(
            new AuthorizationDenial(requiresRole, AuthorizationDenialReason.MissingRole, principal.Id, role),
            new UnauthorizedFailure($"Requires role '{role}'."));
    }

    /// <summary>The scope a request needs when it does not declare one, computed once per behavior instance.</summary>
    public static string? DefaultScope<TRequest>(SynapseAuthorizationOptions options)
    {
        // A public request (sign-in, sign-up) has no scope to give an anonymous caller, so the default does not apply.
        if (typeof(IAllowAnonymous).IsAssignableFrom(typeof(TRequest)))
        {
            return null;
        }

        return options.IntentOf(typeof(TRequest)) switch
        {
            RequestIntent.Command => options.CommandScope,
            RequestIntent.Query => options.QueryScope,
            _ => null
        };
    }

    public static Denied? Scope<TRequest>(TRequest request, ICurrentPrincipal principal, string? defaultScope)
    {
        var scope = request is IRequiresScope requiresScope
            ? AuthorizationDenials.RequireValue<TRequest>(requiresScope.Scope, nameof(IRequiresScope),
                nameof(IRequiresScope.Scope))
            : defaultScope;
        if (scope is null || principal.HasScope(scope))
        {
            return null;
        }

        return new Denied(
            new AuthorizationDenial(request!, AuthorizationDenialReason.MissingScope, principal.Id, scope),
            new UnauthorizedFailure($"Requires scope '{scope}'."));
    }

    public static async ValueTask<Denied?> RequestAsync<TRequest>(TRequest request,
        ICurrentPrincipal principal,
        IRequestAccessPolicy<TRequest> policy,
        CancellationToken cancellationToken)
    {
        if (await policy.IsAllowedAsync(principal, request, cancellationToken))
        {
            return null;
        }

        return new Denied(new AuthorizationDenial(request!, AuthorizationDenialReason.RequestDenied, principal.Id),
            new UnauthorizedFailure("The request was denied by its access policy."));
    }

    public static async ValueTask<Denied?> ResourceAsync<TRequest>(IResourceScoped scoped,
        ICurrentPrincipal principal,
        IResourceAccessPolicy? policy,
        CancellationToken cancellationToken)
    {
        if (scoped is IOptionallyResourceScoped optional)
        {
            if (optional.OptionalResourceId is not { } resourceId)
            {
                return null;
            }

            // Blank is not "no resource": it is what a bug looks like, and skipping the check would fail open.
            if (string.IsNullOrWhiteSpace(resourceId))
            {
                throw new InvalidOperationException(
                    $"{typeof(TRequest).FullName} implements {nameof(IOptionallyResourceScoped)} but its " +
                    $"{nameof(IOptionallyResourceScoped.OptionalResourceId)} is empty or whitespace. Return null when " +
                    "the request targets no resource.");
            }
        }

        var access = policy is null
            ? ResourceAccess.Denied
            : await policy.CheckAsync(principal, scoped, cancellationToken);
        if (access == ResourceAccess.Allowed)
        {
            return null;
        }

        if (access == ResourceAccess.Hidden)
        {
            // A fixed message: the caller must not tell this apart from a resource that does not exist.
            return new Denied(
                new AuthorizationDenial(scoped, AuthorizationDenialReason.ResourceHidden, principal.Id,
                    scoped.Access, scoped.ResourceId),
                new NotFoundFailure("Resource", scoped.ResourceId, "The requested resource was not found."));
        }

        // Any other value, defined or not, is a denial. Fail closed: a resource-scoped request without a policy is a misconfiguration, not an open door.
        var reason = policy is null
            ? $"Access '{scoped.Access}' denied: no {nameof(IResourceAccessPolicy)} is registered."
            : $"Access '{scoped.Access}' to the resource denied.";
        return new Denied(
            new AuthorizationDenial(scoped, AuthorizationDenialReason.ResourceAccessDenied, principal.Id,
                scoped.Access, scoped.ResourceId),
            new UnauthorizedFailure(reason));
    }
}
