using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Publish.Outbox;
using UnambitiousFx.Synapse.Tests.Definitions;

namespace UnambitiousFx.Synapse.Tests.DependencyInjection;

/// <summary>
///     Guards the shape of the context graph rather than any single registration.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="IContext" /> used to sit downstream of the publish stack, because the context exposed
///         publish and commit sugar and so needed <c>IEmitter</c> and <c>IOutboxCommit</c>. That closed a ring —
///         <c>IContext → IContextAccessor → IContextFactory → IEmitter → IOutboxManager</c> — which the outbox had
///         to work around by reading an ambient <c>AsyncLocal</c> instead of injecting
///         <see cref="IContextAccessor" />.
///     </para>
///     <para>
///         <c>ValidateOnBuild</c> is a sanity check here, not the guard: <see cref="IContextAccessor" /> is
///         registered through a factory delegate so that it and the concrete handler share one instance, and the
///         container cannot see through a delegate to find a ring behind it. What actually pins the shape is the
///         assertion that the factory has no constructor dependencies at all — plus the compiler, since the
///         factories are constructed directly in several tests, so giving one a dependency again fails the build.
///     </para>
/// </remarks>
public sealed class ContextGraphValidationTests
{
    [Fact]
    public void AddSynapse_BuildsWithValidationEnabled()
    {
        // Arrange (Given)
        var services = new ServiceCollection()
            .AddSynapse(cfg =>
                cfg.RegisterRequestHandler<RequestWithResponseExampleHandler, RequestWithResponseExample, int>())
            .AddLogging();

        // Act (When)
        var action = () => services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        // Assert (Then)
        using var provider = action();
        Assert.NotNull(provider);
    }

    [Fact]
    public void OutboxManagerAndContext_ResolveFromTheSameScope_WithoutCycling()
    {
        // Arrange (Given) — resolving both is the pairing that used to be impossible: the outbox needs the
        // context to capture propagation headers, and the context used to need the outbox to commit
        using var provider = new ServiceCollection()
            .AddSynapse(cfg =>
                cfg.RegisterRequestHandler<RequestWithResponseExampleHandler, RequestWithResponseExample, int>())
            .AddLogging()
            .BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });

        using var scope = provider.CreateScope();

        // Act (When)
        var outboxManager = scope.ServiceProvider.GetRequiredService<IOutboxManager>();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();

        // Assert (Then)
        Assert.NotNull(outboxManager);
        Assert.NotEmpty(context.TraceId);
    }

    [Fact]
    public void ContextFactory_HasNoConstructorDependencies()
    {
        // Arrange (Given) — the factory is what used to drag IEmitter into the context's dependencies. Keeping it
        // dependency-free is what lets the outbox inject IContextAccessor at all, so assert it directly.
        using var provider = new ServiceCollection()
            .AddSynapse(_ => { })
            .AddLogging()
            .BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });

        using var scope = provider.CreateScope();

        // Act (When)
        var factory = scope.ServiceProvider.GetRequiredService<IContextFactory>();
        var accessor = scope.ServiceProvider.GetRequiredService<IContextAccessor>();

        // Assert (Then) — resolving the accessor must not have created a context; only reading it does
        Assert.Empty(factory.GetType().GetConstructors().Single().GetParameters());
        Assert.False(accessor.IsInitialized);
    }
}
