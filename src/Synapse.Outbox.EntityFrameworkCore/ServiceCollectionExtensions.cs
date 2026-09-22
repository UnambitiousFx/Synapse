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
    /// <typeparam name="TContext">The <see cref="DbContext" /> type that owns the outbox table.</typeparam>
    public static IServiceCollection AddEfCoreEventOutbox<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        return services.AddScoped<EfCoreEventOutboxStorage<TContext>>();
    }
}
