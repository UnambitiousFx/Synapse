using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace UnambitiousFx.Synapse.Outbox.AdoNet;

/// <summary>
///     DI registration helpers for the ADO.NET outbox storage.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Registers <see cref="AdoNetEventOutboxStorage" /> and <see cref="AdoNetOutboxTransaction" /> Scoped. Call
    ///     <c>ISynapseConfig.SetEventOutboxStorage&lt;AdoNetEventOutboxStorage&gt;()</c> inside <c>AddSynapse</c> to
    ///     make it the active outbox storage.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Call this <b>before</b> <c>AddSynapse</c>, so <c>SetEventOutboxStorage</c> forwards
    ///         <c>IEventOutboxStorage</c> to this registration and both resolve to the same instance per scope.
    ///     </para>
    ///     <para>
    ///         Connections come from the <see cref="DbDataSource" /> in DI: Npgsql's <c>AddNpgsqlDataSource</c>
    ///         registers one, and for SQL Server register
    ///         <c>SqlClientFactory.Instance.CreateDataSource(connectionString)</c> as a singleton
    ///         <see cref="DbDataSource" />.
    ///     </para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Chooses the dialect and, optionally, the schema and table name.</param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <exception cref="InvalidOperationException">The configured schema or table name is not a plain identifier.</exception>
    public static IServiceCollection AddAdoNetEventOutbox(this IServiceCollection services,
        Action<AdoNetOutboxOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var options = new AdoNetOutboxOptions();
        configure(options);
        options.Validate();

        services.AddSingleton(options);
        services.TryAddScoped<AdoNetOutboxTransaction>();
        services.AddScoped<AdoNetEventOutboxStorage>();
        return services;
    }
}
