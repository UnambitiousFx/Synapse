using Microsoft.Extensions.DependencyInjection;

namespace UnambitiousFx.Synapse;

/// <summary>
///     Defines a configuration interface for adding service configurations to an
///     <see cref="IServiceCollection" />, enabling service setup for specific application functionalities.
/// </summary>
public interface IServiceConfigurator
{
    /// <summary>
    ///     Adds a configuration action to the collection of service configurations
    ///     for distributed event handling.
    /// </summary>
    /// <param name="configureServices">
    ///     An action that configures the IServiceCollection instance with the services
    ///     required for distributed event functionality.
    /// </param>
    void Add(Action<IServiceCollection> configureServices);
}