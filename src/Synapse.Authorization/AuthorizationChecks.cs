using UnambitiousFx.Functional.Failures;

namespace UnambitiousFx.Synapse.Authorization;

/// <summary>A denied check: what to report to the recorders, and what to return to the caller.</summary>
internal readonly record struct Denied(AuthorizationDenial Denial, Failure Failure);

/// <summary>
///     The four authorization checks, shared by the request behaviors and the stream behavior so both pipelines apply
///     exactly the same rules. Each returns <see langword="null" /> when the request may go on.
/// </summary>
internal static class AuthorizationChecks
{
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

        return RequestIntentOf<TRequest>.Value switch
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

    public static async ValueTask<Denied?> ResourceAsync(IResourceScoped scoped,
        ICurrentPrincipal principal,
        IResourceAccessPolicy? policy,
        CancellationToken cancellationToken)
    {
        if (policy is not null && await policy.IsAllowedAsync(principal, scoped, cancellationToken))
        {
            return null;
        }

        // Fail closed: a resource-scoped request without a policy is a misconfiguration, not an open door.
        var reason = policy is null
            ? $"Access '{scoped.Access}' denied: no {nameof(IResourceAccessPolicy)} is registered."
            : $"Access '{scoped.Access}' to the resource denied.";
        return new Denied(
            new AuthorizationDenial(scoped, AuthorizationDenialReason.ResourceAccessDenied, principal.Id,
                scoped.Access, scoped.ResourceId),
            new UnauthorizedFailure(reason));
    }
}
