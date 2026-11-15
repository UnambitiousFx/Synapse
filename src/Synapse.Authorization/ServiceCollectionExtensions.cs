using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Authorization;

/// <summary>
///     Registers the authorization request behaviors.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Authorizes every request, for both <c>Result</c> and <c>Result&lt;T&gt;</c> requests, in this order: reject
    ///     anonymous callers (<c>UnauthenticatedFailure</c>) unless the request is <see cref="IAllowAnonymous" />, then
    ///     check <see cref="IRequiresRole" />, then the scope (the request's <see cref="IRequiresScope" />, or the default
    ///     query / command scope), then <see cref="IResourceScoped" /> against the <see cref="IResourceAccessPolicy" />.
    ///     A denial returns an <c>UnauthorizedFailure</c>, is reported to every
    ///     <see cref="IAuthorizationDenialRecorder" />, and the handler is not called. Stream requests get the same checks
    ///     through <see cref="StreamAuthorizationBehavior{TRequest,TItem}" />, a stream counting as a query.
    /// </summary>
    /// <remarks>
    ///     The application registers its <see cref="ICurrentPrincipal" /> itself, usually as a scoped service. Calling
    ///     this method several times (for example from a shared module, then from the host) configures one shared
    ///     <see cref="SynapseAuthorizationOptions" />: every call's settings apply, the last one winning per setting.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optionally sets the default scopes, the resource policy and the denial recorders.</param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <exception cref="ArgumentException">
    ///     <see cref="SynapseAuthorizationOptions.QueryScope" /> or <see cref="SynapseAuthorizationOptions.CommandScope" />
    ///     is empty or whitespace. Use <see langword="null" /> to turn a default scope off.
    /// </exception>
    public static IServiceCollection AddSynapseAuthorization(this IServiceCollection services,
        Action<SynapseAuthorizationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = GetOrAddOptions(services);
        configure?.Invoke(options);
        options.Validate();
        options.ApplyRegistrations(services);
        // Scoped, like the ICurrentPrincipal they read, which is per request / per call.
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResultPipelineBehavior<,>),
            typeof(ActorAuthorizationBehavior<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResultPipelineBehavior<,>),
            typeof(RoleAuthorizationBehavior<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResultPipelineBehavior<,>),
            typeof(ScopeAuthorizationBehavior<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResultPipelineBehavior<,>),
            typeof(ResourceAuthorizationBehavior<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IStreamRequestPipelineBehavior<,>),
            typeof(StreamAuthorizationBehavior<,>)));
        return services;
    }

    /// <summary>
    ///     Registers a scoped <see cref="ClaimsCurrentPrincipal" /> as the <see cref="ICurrentPrincipal" />, over the
    ///     <see cref="ClaimsPrincipal" /> that <paramref name="user" /> returns in each scope. In ASP.NET Core:
    ///     <code>
    ///     services.AddHttpContextAccessor();
    ///     services.AddSynapseClaimsPrincipal(sp =&gt; sp.GetRequiredService&lt;IHttpContextAccessor&gt;().HttpContext?.User);
    ///     </code>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="user">Returns the current caller, or <see langword="null" /> when there is none.</param>
    /// <param name="configure">Optionally changes the claim types read for scopes, roles and the id.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddSynapseClaimsPrincipal(this IServiceCollection services,
        Func<IServiceProvider, ClaimsPrincipal?> user,
        Action<ClaimsCurrentPrincipalOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(user);
        var options = new ClaimsCurrentPrincipalOptions();
        configure?.Invoke(options);
        services.AddScoped<ICurrentPrincipal>(provider => new ClaimsCurrentPrincipal(user(provider), options));
        return services;
    }

    // Repeated calls must configure the same instance: keeping only the first call's options would silently drop a
    // later, stricter scope while that call's policy still applied.
    private static SynapseAuthorizationOptions GetOrAddOptions(IServiceCollection services)
    {
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType == typeof(SynapseAuthorizationOptions) &&
                descriptor.ImplementationInstance is SynapseAuthorizationOptions existing)
            {
                return existing;
            }
        }

        var options = new SynapseAuthorizationOptions();
        services.AddSingleton(options);
        return options;
    }
}
