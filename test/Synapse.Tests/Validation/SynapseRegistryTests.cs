using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Validation;

namespace UnambitiousFx.Synapse.Tests.Validation;

[TestSubject(typeof(SynapseRegistry))]
public sealed class SynapseRegistryTests
{
    [Fact]
    public void Create_WithHandlersRegisteredDirectly_RecordsAProbePerRequestAndEvent()
    {
        // Arrange (Given)
        using var provider = Build(cfg =>
        {
            cfg.RegisterRequestHandler<PlainHandler, PlainCommand>();
            cfg.RegisterRequestHandler<CountHandler, CountQuery, int>();
            cfg.RegisterEventHandler<PingHandler, PingEvent>();
        });

        // Act (When)
        var registry = provider.GetRequiredService<SynapseRegistry>();

        // Assert (Then)
        Assert.Contains(typeof(PlainCommand), registry.Probes.Keys);
        Assert.Contains(typeof(CountQuery), registry.Probes.Keys);
        Assert.Contains(typeof(PingEvent), registry.Probes.Keys);
        Assert.Equal(1, registry.RequestHandlerCounts[typeof(PlainCommand)]);
        Assert.Equal(1, registry.RequestHandlerCounts[typeof(CountQuery)]);
        Assert.Contains(typeof(PingEvent), registry.EventsWithHandlers);
    }

    [Fact]
    public void Create_WithHandlersRegisteredThroughAGroup_RecordsTheirProbes()
    {
        // Arrange (Given)
        using var provider = Build(cfg => cfg.AddRegisterGroup(new TestGroup()));

        // Act (When)
        var registry = provider.GetRequiredService<SynapseRegistry>();

        // Assert (Then)
        Assert.Contains(typeof(PlainCommand), registry.Probes.Keys);
        Assert.Contains(typeof(PingEvent), registry.Probes.Keys);
    }

    [Fact]
    public void Create_WithAConditionalHandlerWhoseConditionIsFalse_RecordsNoProbe()
    {
        // Arrange (Given)
        using var provider = Build(cfg => cfg.RegisterRequestHandlerWhen<PlainHandler, PlainCommand>(() => false));

        // Act (When)
        var registry = provider.GetRequiredService<SynapseRegistry>();

        // Assert (Then)
        Assert.DoesNotContain(typeof(PlainCommand), registry.Probes.Keys);
        Assert.DoesNotContain(typeof(PlainCommand), registry.RequestHandlerCounts.Keys);
    }

    [Fact]
    public void Create_WithTwoHandlersForOneRequest_CountsBoth()
    {
        // Arrange (Given)
        using var provider = Build(cfg =>
        {
            cfg.RegisterRequestHandler<PlainHandler, PlainCommand>();
            cfg.RegisterRequestHandler<OtherPlainHandler, PlainCommand>();
        });

        // Act (When)
        var registry = provider.GetRequiredService<SynapseRegistry>();

        // Assert (Then)
        Assert.Equal(2, registry.RequestHandlerCounts[typeof(PlainCommand)]);
    }

    [Fact]
    public void Create_WithClosedBehaviors_RecordsTheTypesTheyTarget()
    {
        // Arrange (Given)
        using var provider = Build(cfg =>
        {
            cfg.RegisterRequestPipelineBehavior<NoopBehavior<PlainCommand>, PlainCommand>();
            cfg.RegisterRequestPipelineBehavior<NoopResponseBehavior, CountQuery, int>();
            cfg.RegisterEventPipelineBehavior<NoopEventBehavior, PingEvent>();
        });

        // Act (When)
        var registry = provider.GetRequiredService<SynapseRegistry>();

        // Assert (Then)
        Assert.Contains(typeof(PlainCommand), registry.RequestsWithBehaviors);
        Assert.Contains(typeof(CountQuery), registry.RequestsWithBehaviors);
        Assert.Contains(typeof(PingEvent), registry.EventsWithBehaviors);
    }

    [Fact]
    public void Create_WithAnOpenGenericBehavior_IgnoresIt()
    {
        // Arrange (Given) — DI closes open generics lazily over any request, so they target nothing in particular
        using var provider = Build(cfg =>
        {
            cfg.RegisterRequestHandler<PlainHandler, PlainCommand>();
            cfg.AddOpenGenericRequestPipelineBehavior(typeof(NoopBehavior<>));
        });

        // Act (When)
        var registry = provider.GetRequiredService<SynapseRegistry>();

        // Assert (Then)
        Assert.Empty(registry.RequestsWithBehaviors);
    }

    [Fact]
    public async Task Probe_WhenInvoked_DescribesTheRegisteredPipeline()
    {
        // Arrange (Given)
        await using var provider = Build(cfg =>
        {
            cfg.RegisterRequestHandler<PlainHandler, PlainCommand>();
            cfg.RegisterRequestPipelineBehavior<NoopBehavior<PlainCommand>, PlainCommand>();
        });
        var registry = provider.GetRequiredService<SynapseRegistry>();

        // Act (When)
        var description = registry.Probes[typeof(PlainCommand)](provider.GetRequiredService<IPipelineDescriber>());

        // Assert (Then)
        Assert.NotNull(description);
        Assert.Equal(typeof(PlainHandler), Assert.Single(description.Handlers));
        Assert.Equal(typeof(NoopBehavior<PlainCommand>), Assert.Single(description.Behaviors).Type);
    }

    private static ServiceProvider Build(Action<ISynapseConfig> configure)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSynapse(configure);
        return services.BuildServiceProvider();
    }

    private sealed record PlainCommand : IRequest;

    private sealed record CountQuery : IRequest<int>;

    private sealed record PingEvent : IEvent;

    private sealed class PlainHandler : IRequestHandler<PlainCommand>
    {
        public ValueTask<Result> HandleAsync(PlainCommand request, CancellationToken cancellationToken = default) =>
            new(Result.Success());
    }

    private sealed class OtherPlainHandler : IRequestHandler<PlainCommand>
    {
        public ValueTask<Result> HandleAsync(PlainCommand request, CancellationToken cancellationToken = default) =>
            new(Result.Success());
    }

    private sealed class CountHandler : IRequestHandler<CountQuery, int>
    {
        public ValueTask<Result<int>> HandleAsync(CountQuery request, CancellationToken cancellationToken = default) =>
            new(Result.Success(1));
    }

    private sealed class PingHandler : IEventHandler<PingEvent>
    {
        public ValueTask<Result> HandleAsync(PingEvent @event, CancellationToken cancellationToken = default) =>
            new(Result.Success());
    }

    private sealed class NoopBehavior<TRequest> : IRequestPipelineBehavior<TRequest>
        where TRequest : IRequest
    {
        public ValueTask<Result> HandleAsync(TRequest request, RequestHandlerDelegate<TRequest> next,
            CancellationToken cancellationToken = default) => next(request, cancellationToken);
    }

    private sealed class NoopResponseBehavior : IRequestPipelineBehavior<CountQuery, int>
    {
        public ValueTask<Result<int>> HandleAsync(CountQuery request, RequestHandlerDelegate<CountQuery, int> next,
            CancellationToken cancellationToken = default) => next(request, cancellationToken);
    }

    private sealed class NoopEventBehavior : IEventPipelineBehavior<PingEvent>
    {
        public ValueTask<Result> HandleAsync(PingEvent @event, EventHandlerDelegate<PingEvent> next,
            CancellationToken cancellationToken = default) => next(@event, cancellationToken);
    }

    private sealed class TestGroup : IRegisterGroup
    {
        public void Register(IDependencyInjectionBuilder builder)
        {
            builder.RegisterRequestHandler<PlainHandler, PlainCommand>();
            builder.RegisterEventHandler<PingHandler, PingEvent>();
        }
    }
}
