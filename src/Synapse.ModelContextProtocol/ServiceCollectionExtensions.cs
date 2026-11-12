using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace UnambitiousFx.Synapse.ModelContextProtocol;

/// <summary>
///     Extension methods for registering the Synapse Model Context Protocol integration.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Registers <see cref="IMcpInvoker" /> and the default <see cref="IMcpFailurePolicy" />.
    /// </summary>
    /// <remarks>
    ///     Synapse itself must be registered too (<c>AddSynapse</c>), as must the MCP server
    ///     (<c>AddMcpServer().WithTools&lt;T&gt;()</c> from the <c>ModelContextProtocol</c> package). Calling this
    ///     more than once is safe: each <paramref name="configure" /> is applied in order.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An optional delegate to configure <see cref="SynapseMcpOptions" />.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSynapseMcp(this IServiceCollection services,
        Action<SynapseMcpOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = services.AddOptions<SynapseMcpOptions>();
        if (configure is not null)
        {
            options.Configure(configure);
        }

        services.TryAddSingleton<IMcpFailurePolicy, DefaultMcpFailurePolicy>();
        services.TryAddScoped<IMcpInvoker, McpInvoker>();
        return services;
    }
}
