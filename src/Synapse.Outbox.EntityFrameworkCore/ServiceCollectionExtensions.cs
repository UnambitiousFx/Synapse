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
    ///     <para>
    ///         <b>Reach the storage through <c>IOutboxDiscard</c> / <c>IEventOutboxStorage</c>, never by
    ///         injecting <see cref="EfCoreEventOutboxStorage{TContext}" /> directly.</b> This method
    ///         registers the concrete type as its own service, and
    ///         <c>SetEventOutboxStorage&lt;EfCoreEventOutboxStorage&lt;TContext&gt;&gt;()</c> registers
    ///         <c>IEventOutboxStorage</c> as a separate, independent descriptor. The container activates the
    ///         two independently, so one scope ends up holding <em>two</em> storage instances over the same
    ///         <typeparamref name="TContext" />.
    ///     </para>
    ///     <para>
    ///         That matters for <c>DiscardAsync</c> only, which matches events by reference against what
    ///         <c>AddAsync</c> recorded on that same instance. An instance obtained by injecting the
    ///         concrete type has never seen the events the application stored through
    ///         <c>IEventOutboxStorage</c>, so every discard through it silently no-ops. Every other
    ///         operation is keyed by row id or queried from the database, so it behaves identically on
    ///         either instance.
    ///     </para>
    /// </remarks>
    /// <typeparam name="TContext">The <see cref="DbContext" /> type that owns the outbox table.</typeparam>
    public static IServiceCollection AddEfCoreEventOutbox<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        return services.AddScoped<EfCoreEventOutboxStorage<TContext>>();
    }
}
