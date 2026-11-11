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
    ///     anonymous callers (<c>UnauthenticatedFailure</c>), then check <see cref="IRequiresRole" />, then the scope
    ///     (the request's <see cref="IRequiresScope" />, or the default query / command scope), then
    ///     <see cref="IResourceScoped" /> against the <see cref="IResourceAccessPolicy" />. A denial returns an
    ///     <c>UnauthorizedFailure</c>, is reported to every <see cref="IAuthorizationDenialRecorder" />, and the handler
    ///     is not called.
    /// </summary>
    /// <remarks>
    ///     The application registers its <see cref="ICurrentPrincipal" /> itself, usually as a scoped service.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optionally sets the default scopes, the resource policy and the denial recorders.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddSynapseAuthorization(this IServiceCollection services,
        Action<SynapseAuthorizationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new SynapseAuthorizationOptions();
        configure?.Invoke(options);
        options.ApplyRegistrations(services);
        services.TryAddSingleton(options);
        // Scoped, like the ICurrentPrincipal they read, which is per request / per call.
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResultPipelineBehavior<,>),
            typeof(ActorAuthorizationBehavior<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResultPipelineBehavior<,>),
            typeof(RoleAuthorizationBehavior<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResultPipelineBehavior<,>),
            typeof(ScopeAuthorizationBehavior<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResultPipelineBehavior<,>),
            typeof(ResourceAuthorizationBehavior<,>)));
        return services;
    }
}
