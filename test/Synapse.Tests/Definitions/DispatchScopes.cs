using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Contexts;

namespace UnambitiousFx.Synapse.Tests.Definitions;

/// <summary>
///     Builds the <see cref="IServiceScopeFactory" /> a component uses when it runs work in a scope of its own.
/// </summary>
/// <remarks>
///     The scopes carry the real context wiring — <see cref="IInboundContextStore" />, the context factory and
///     <c>ContextHandler</c> — so a child scope's <see cref="IContext" /> is built from whatever the component
///     wrote to the store, exactly as it would be in an application. A substitute scope factory could not show
///     that: the whole point of the child scope is which context the code inside it sees.
/// </remarks>
public static class DispatchScopes
{
    /// <summary>
    ///     A factory whose scopes resolve <paramref name="dispatcher" /> as their <see cref="IEventDispatcher" />.
    /// </summary>
    /// <param name="dispatcher">
    ///     Builds the dispatcher from the scope's own provider, so a test can capture what that scope resolves.
    ///     Defaults to a substitute.
    /// </param>
    /// <remarks>
    ///     The root provider is deliberately not disposed. It owns nothing but the scoped registrations below,
    ///     each scope disposes its own instances, and returning a bare factory keeps the callers — of which there
    ///     are many — from each having to hold a disposable they do not otherwise use.
    /// </remarks>
    public static IServiceScopeFactory For(Func<IServiceProvider, IEventDispatcher>? dispatcher = null)
    {
        var services = new ServiceCollection();
        services.AddScoped<IContextFactory, DefaultContextFactory>();
        services.AddScoped<IInboundContextStore, InboundContextStore>();
        services.AddScoped<ContextHandler>();
        services.AddScoped<IContextAccessor>(sp => sp.GetRequiredService<ContextHandler>());
        services.AddScoped(sp => sp.GetRequiredService<IContextAccessor>().Context);
        services.AddScoped(dispatcher ?? (_ => Substitute.For<IEventDispatcher>()));

        return services.BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();
    }
}
