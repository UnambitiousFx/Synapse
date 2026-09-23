using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
}
