using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UnambitiousFx.Functional.AspNetCore.Mappers;
using UnambitiousFx.Synapse.AspNetCore.Http;
using UnambitiousFx.Synapse.AspNetCore.Mvc;

namespace UnambitiousFx.Synapse.AspNetCore;

/// <summary>
///     Extension methods for registering Synapse ASP.NET Core integration services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Adds Synapse ASP.NET Core integration services to the service collection.
    ///     Registers <see cref="IHttpInvoker" />, <see cref="IMvcInvoker" />, and
    ///     the default <see cref="IFailureHttpMapper" />.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSynapseAspNetCore(this IServiceCollection services)
    {
        services.AddOptions<SynapseAspNetCoreOptions>();
        services.TryAddSingleton<IFailureHttpMapper, DefaultFailureHttpMapper>();
        services.TryAddScoped<IHttpInvoker, HttpInvoker>();
        services.TryAddScoped<IMvcInvoker, MvcInvoker>();
        return services;
    }

    /// <summary>
    ///     Adds Synapse ASP.NET Core integration services and configures <see cref="SynapseAspNetCoreOptions" />, for
    ///     instance the <c>WWW-Authenticate</c> challenge written on 401 responses.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSynapseAspNetCore(this IServiceCollection services,
        Action<SynapseAspNetCoreOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        services.Configure(configure);
        return services.AddSynapseAspNetCore();
    }
}