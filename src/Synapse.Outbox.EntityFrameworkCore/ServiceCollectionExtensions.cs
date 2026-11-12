using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace UnambitiousFx.Synapse.Outbox.EntityFrameworkCore;

/// <summary>
///     DI registration helpers for the EF Core outbox storage.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Registers <see cref="EfCoreEventOutboxStorage{TContext}" /> Scoped for the given context
    ///     type. Call
    ///     <c>ISynapseConfig.SetEventOutboxStorage&lt;EfCoreEventOutboxStorage&lt;TContext&gt;&gt;()</c>
    ///     inside <c>AddSynapse</c> to wire it as the active outbox storage.
    /// </summary>
    /// <remarks>
    ///     Call this <b>before</b> <c>AddSynapse</c>.
    ///     <c>SetEventOutboxStorage&lt;EfCoreEventOutboxStorage&lt;TContext&gt;&gt;()</c> forwards
    ///     <c>IEventOutboxStorage</c> to whatever concrete-type registration already exists for
    ///     <see cref="EfCoreEventOutboxStorage{TContext}" />, so both resolve to the same instance per
    ///     scope — but only if that registration exists yet. Called after <c>AddSynapse</c>, there is
    ///     nothing to forward to and <c>IEventOutboxStorage</c> falls back to constructing its own,
    ///     independent instance, so a directly-injected <see cref="EfCoreEventOutboxStorage{TContext}" />
    ///     never sees what <c>IEventOutboxStorage</c> consumers wrote.
    /// </remarks>
    /// <typeparam name="TContext">The <see cref="DbContext" /> type that owns the outbox table.</typeparam>
    public static IServiceCollection AddEfCoreEventOutbox<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        return services.AddScoped<EfCoreEventOutboxStorage<TContext>>();
    }

    /// <summary>
    ///     Configures <see cref="EfCoreTransactionManager" />: the contexts every transaction spans and whether the
    ///     outbox is flushed after the outermost commit. Then wire it as the transaction manager with
    ///     <c>AddSynapse(cfg =&gt; cfg.AddTransactions&lt;EfCoreTransactionManager&gt;())</c>.
    /// </summary>
    /// <remarks>
    ///     <code>
    ///     services.AddEfCoreTransactions(t =&gt; t
    ///         .Enlist&lt;EnvelopesDbContext&gt;()
    ///         .Enlist&lt;OutboxDbContext&gt;());
    ///     </code>
    ///     Every enlisted context must be built on the same Scoped <see cref="System.Data.Common.DbConnection" />.
    ///     A later call replaces the options of an earlier one.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Enlists the contexts, in the order the first one opens the transaction.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddEfCoreTransactions(this IServiceCollection services,
        Action<EfCoreTransactionOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var options = new EfCoreTransactionOptions();
        configure(options);
        if (options.ContextTypes.Count == 0)
        {
            throw new ArgumentException(
                $"Enlist at least one DbContext with {nameof(EfCoreTransactionOptions.Enlist)}<TContext>().",
                nameof(configure));
        }

        services.Replace(ServiceDescriptor.Singleton(options));
        return services;
    }
}
