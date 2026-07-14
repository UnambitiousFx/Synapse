using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Observability;
using UnambitiousFx.Synapse.Publish;
using UnambitiousFx.Synapse.Publish.Orchestrators;
using UnambitiousFx.Synapse.Resolvers;
using UnambitiousFx.Synapse.Tests.Definitions;

namespace UnambitiousFx.Synapse.Tests.Publish;

[TestSubject(typeof(EventDispatcher))]
public sealed class EventDispatcherTests
{
    private readonly IDependencyResolver _dependencyResolver;
    private readonly IEventOrchestrator _eventOrchestrator;
    private readonly ILogger<EventDispatcher> _logger;
    private readonly ISynapseMetrics _metrics;
    private readonly EventDispatcherOptions _options;

    public EventDispatcherTests()
    {
        _dependencyResolver = Substitute.For<IDependencyResolver>();
        _eventOrchestrator = Substitute.For<IEventOrchestrator>();
        _logger = Substitute.For<ILogger<EventDispatcher>>();
        _metrics = Substitute.For<ISynapseMetrics>();
        _options = new EventDispatcherOptions
        {
            DispatchStrategy = DispatchStrategy.Immediate
        };
    }

    private EventDispatcher CreateDispatcher(EventDispatcherOptions? options = null)
    {
        return new EventDispatcher(
            _dependencyResolver,
            _metrics,
            _eventOrchestrator,
            Options.Create(options ?? _options),
            _logger);
    }

    [Fact]
    public async Task DispatchAsync_ExecutesHandlers()
    {
        // Arrange (Given)
        var @event = new EventExample("Test Event");
        var handler = new EventExampleHandler1();
        var dispatcher = CreateDispatcher();

        _dependencyResolver.GetServices<IEventHandler<EventExample>>()
            .Returns(new[] { handler });
        _dependencyResolver.GetServices<IEventPipelineBehavior<EventExample>>()
            .Returns(Array.Empty<IEventPipelineBehavior<EventExample>>());
        _eventOrchestrator.RunAsync(Arg.Any<IEventHandler<EventExample>[]>(), @event, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        // Act (When)
        var result = await dispatcher.DispatchAsync(@event, TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        await _eventOrchestrator.Received(1).RunAsync(
            Arg.Is<IEventHandler<EventExample>[]>(h => h != null && h.Length == 1),
            @event,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchAsync_WithMultipleHandlers_ExecutesAllHandlers()
    {
        // Arrange (Given)
        var @event = new EventExample("Test Event");
        var handler1 = new EventExampleHandler1();
        var handler2 = new EventExampleHandler2();
        var dispatcher = CreateDispatcher();

        _dependencyResolver.GetServices<IEventHandler<EventExample>>()
            .Returns(new IEventHandler<EventExample>[] { handler1, handler2 });
        _dependencyResolver.GetServices<IEventPipelineBehavior<EventExample>>()
            .Returns(Array.Empty<IEventPipelineBehavior<EventExample>>());
        _eventOrchestrator.RunAsync(Arg.Any<IEventHandler<EventExample>[]>(), @event, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        // Act (When)
        var result = await dispatcher.DispatchAsync(@event, TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        await _eventOrchestrator.Received(1).RunAsync(
            Arg.Is<IEventHandler<EventExample>[]>(h => h != null && h.Length == 2),
            @event,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchAsync_WithNoHandlers_CompletesSuccessfully()
    {
        // Arrange (Given)
        var @event = new EventExample("Test Event");
        var dispatcher = CreateDispatcher();

        _dependencyResolver.GetServices<IEventHandler<EventExample>>()
            .Returns(Array.Empty<IEventHandler<EventExample>>());
        _dependencyResolver.GetServices<IEventPipelineBehavior<EventExample>>()
            .Returns(Array.Empty<IEventPipelineBehavior<EventExample>>());
        _eventOrchestrator.RunAsync(Arg.Any<IEventHandler<EventExample>[]>(), @event, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        // Act (When)
        var result = await dispatcher.DispatchAsync(@event, TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        await _eventOrchestrator.Received(1).RunAsync(
            Arg.Is<IEventHandler<EventExample>[]>(h => h != null && h.Length == 0),
            @event,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchAsync_WithPipelineBehaviors_ExecutesPipelineBeforeHandlers()
    {
        // Arrange (Given)
        var @event = new EventExample("Test Event");
        var handler = new EventExampleHandler1();
        var behavior = Substitute.For<IEventPipelineBehavior<EventExample>>();
        var dispatcher = CreateDispatcher();

        _dependencyResolver.GetServices<IEventHandler<EventExample>>()
            .Returns(new[] { handler });
        _dependencyResolver.GetServices<IEventPipelineBehavior<EventExample>>()
            .Returns(new[] { behavior });

        behavior.HandleAsync(@event, Arg.Any<EventHandlerDelegate<EventExample>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var next = callInfo.Arg<EventHandlerDelegate<EventExample>>();
                return next!(@event, TestContext.Current.CancellationToken);
            });

        _eventOrchestrator.RunAsync(Arg.Any<IEventHandler<EventExample>[]>(), @event, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        // Act (When)
        var result = await dispatcher.DispatchAsync(@event, TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        await behavior.Received(1).HandleAsync(@event, Arg.Any<EventHandlerDelegate<EventExample>>(), Arg.Any<CancellationToken>());
        await _eventOrchestrator.Received(1).RunAsync(
            Arg.Any<IEventHandler<EventExample>[]>(),
            @event,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchAsync_CalledTwiceOnSameInstance_ResolvesAndSortsBehaviorsOnce()
    {
        // Arrange (Given)
        var @event = new EventExample("Test Event");
        var handler = new EventExampleHandler1();
        var dispatcher = CreateDispatcher();

        _dependencyResolver.GetServices<IEventHandler<EventExample>>()
            .Returns(new[] { handler });
        _dependencyResolver.GetServices<IEventPipelineBehavior<EventExample>>()
            .Returns(Array.Empty<IEventPipelineBehavior<EventExample>>());
        _eventOrchestrator.RunAsync(Arg.Any<IEventHandler<EventExample>[]>(), @event, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        // Act (When)
        await dispatcher.DispatchAsync(@event, TestContext.Current.CancellationToken);
        await dispatcher.DispatchAsync(@event, TestContext.Current.CancellationToken);

        // Assert (Then) — the sorted behavior array is cached per event type for the dispatcher's
        // lifetime, so behaviors are resolved only once across both dispatches.
        _dependencyResolver.Received(1).GetServices<IEventPipelineBehavior<EventExample>>();
    }

    [Fact]
    public async Task DispatchAsync_WithPolymorphicEvent_UsesDispatcherDelegate()
    {
        // Arrange (Given)
        var @event = new InheritedEventExample("Test Event");
        var handler = Substitute.For<IEventHandler<InheritedEventExample>>();
        var dispatcher = CreateDispatcher(new EventDispatcherOptions
        {
            DispatchStrategy = DispatchStrategy.Immediate,
            Dispatchers = new Dictionary<Type, DispatchEventDelegate>
            {
                [typeof(InheritedEventExample)] = (@event, eventDispatcher, ct) =>
                {
                    var typedEvent = (InheritedEventExample)@event;
                    return eventDispatcher.DispatchAsync(typedEvent, ct);
                }
            }
        });

        _dependencyResolver.GetServices<IEventHandler<InheritedEventExample>>()
            .Returns(new[] { handler });
        _dependencyResolver.GetServices<IEventPipelineBehavior<InheritedEventExample>>()
            .Returns(Array.Empty<IEventPipelineBehavior<InheritedEventExample>>());

        handler.HandleAsync(Arg.Any<InheritedEventExample>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _eventOrchestrator.RunAsync(Arg.Any<IEventHandler<InheritedEventExample>[]>(), Arg.Any<InheritedEventExample>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        // Act (When) - Dispatch as base type
        var result = await dispatcher.DispatchAsync<BaseEventExample>(@event, TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        await _eventOrchestrator.Received(1).RunAsync(
            Arg.Any<IEventHandler<InheritedEventExample>[]>(),
            Arg.Any<InheritedEventExample>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchAsync_RecordsMetrics()
    {
        // Arrange (Given)
        var @event = new EventExample("Test Event");
        var handler = new EventExampleHandler1();
        var dispatcher = CreateDispatcher();

        _dependencyResolver.GetServices<IEventHandler<EventExample>>()
            .Returns(new[] { handler });
        _dependencyResolver.GetServices<IEventPipelineBehavior<EventExample>>()
            .Returns(Array.Empty<IEventPipelineBehavior<EventExample>>());
        _eventOrchestrator.RunAsync(Arg.Any<IEventHandler<EventExample>[]>(), @event, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        // Act (When)
        var result = await dispatcher.DispatchAsync(@event, TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        _metrics.Received(1).RecordDispatchLatency(Arg.Any<double>(), Arg.Any<string>());
        _metrics.Received(1).RecordEventDispatched(Arg.Any<string>(), true);
    }

    [Fact]
    public async Task DispatchAsync_OnFailure_RecordsFailureMetrics()
    {
        // Arrange (Given)
        var @event = new EventExample("Test Event");
        var handler = new EventExampleHandler1();
        var dispatcher = CreateDispatcher();

        _dependencyResolver.GetServices<IEventHandler<EventExample>>()
            .Returns(new[] { handler });
        _dependencyResolver.GetServices<IEventPipelineBehavior<EventExample>>()
            .Returns(Array.Empty<IEventPipelineBehavior<EventExample>>());
        _eventOrchestrator.RunAsync(Arg.Any<IEventHandler<EventExample>[]>(), @event, Arg.Any<CancellationToken>())
            .Returns(Result.Failure("Test failure"));

        // Act (When)
        var result = await dispatcher.DispatchAsync(@event, TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.False(result.IsSuccess);
        _metrics.Received(1).RecordDispatchLatency(Arg.Any<double>(), Arg.Any<string>());
        _metrics.Received(1).RecordEventDispatched(Arg.Any<string>(), false);
    }

    [Fact]
    public async Task DispatchAsync_WithPolymorphicEvent_AndNoRegisteredDispatcher_ReturnsFailure()
    {
        // Arrange (Given) — empty Dispatchers map: no delegate registered for InheritedEventExample
        var @event = new InheritedEventExample("Test Event");
        var dispatcher = CreateDispatcher(new EventDispatcherOptions
        {
            DispatchStrategy = DispatchStrategy.Immediate,
            Dispatchers = new Dictionary<Type, DispatchEventDelegate>()
        });

        // Act (When) — dispatch as base type so runtimeType != genericType path is taken
        var result = await dispatcher.DispatchAsync<BaseEventExample>(@event, TestContext.Current.CancellationToken);

        // Assert (Then) — no dispatcher registered → Result.Failure
        Assert.False(result.IsSuccess);
    }
}
