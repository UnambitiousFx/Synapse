using Microsoft.Extensions.DependencyInjection;

namespace UnambitiousFx.Synapse.Endpoints.Testing;

/// <summary>
///     Configures a harness before its pipeline is built.
/// </summary>
public sealed class EndpointHarnessOptions
{
    /// <summary>
    ///     Gets the services the endpoint resolves from, pre-seeded with routing, logging, options and
    ///     the Synapse ASP.NET Core services.
    /// </summary>
    /// <remarks>
    ///     Register whatever the endpoint reads through <c>context.Service&lt;T&gt;()</c> here. JSON
    ///     serialization is configured the same way it is in an application, with
    ///     <c>ConfigureHttpJsonOptions</c>.
    /// </remarks>
    public IServiceCollection Services { get; } = new ServiceCollection();
}
