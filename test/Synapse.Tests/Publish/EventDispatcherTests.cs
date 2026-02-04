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
    private readonly ITransportDispatcher _transportDispatcher;

    public EventDispatcherTests()
    {
        _dependencyResolver = Substitute.For<IDependencyResolver>();
        _eventOrchestrator = Substitute.For<IEventOrchestrator>();
        _logger = Substitute.For<ILogger<EventDispatcher>>();
        _metrics = Substitute.For<ISynapseMetrics>();
        _transportDispatcher = Substitute.For<ITransportDispatcher>();
        _options = new EventDispatcherOptions
        {
            DefaultDistributionMode = DistributionMode.Local
        };
    }

    private EventDispatcher CreateDispatcher(
        IEnumerable<IEventRoutingFilter>? routingFilters = null,
        IEnumerable<IPublishEventTrait>? publishEventTraits = null,
        EventDispatcherOptions? options = null)
    {
        return new EventDispatcher(
            _dependencyResolver,
            routingFilters ?? Array.Empty<IEventRoutingFilter>(),
            publishEventTraits ?? Array.Empty<IPublishEventTrait>(),
            _metrics,
            _eventOrchestrator,
            _transportDispatcher,
            Options.Create(options ?? _options),
            _logger);
    }

    [Fact]
    public async Task DispatchAsync_WithLocalMode_ExecutesLocalHandlers()
    {
        // Arrange (Given)
        var @event = new EventExample("Test Event");
        var handler = new EventExampleHandler1();
        var dispatcher = CreateDispatcher();

        _dependencyResolver.GetServices<IEventHandler<EventExample>>()
            .Returns(new[] { handler });
        _dependencyResolver.GetServices<IEventPipelineBehavior>()
            .Returns(Array.Empty<IEventPipelineBehavior>());
        _eventOrchestrator.RunAsync(Arg.Any<IEventHandler<EventExample>[]>(), @event, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        // Act (When)
        var result = await dispatcher.DispatchAsync(@event, DistributionMode.Local, CancellationToken.None);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        await _eventOrchestrator.Received(1).RunAsync(
            Arg.Is<IEventHandler<EventExample>[]>(h => h.Length == 1),
            @event,
            Arg.Any<CancellationToken>());
        await _transportDispatcher.DidNotReceive().DispatchAsync(Arg.Any<EventExample>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchAsync_WithExternalMode_ExecutesExternalTransport()
    {
        // Arrange (Given)
        var @event = new EventExample("Test Event");
        var dispatcher = CreateDispatcher();

        _dependencyResolver.GetServices<IEventHandler<EventExample>>()
            .Returns(Array.Empty<IEventHandler<EventExample>>());
        _dependencyResolver.GetServices<IEventPipelineBehavior>()
            .Returns(Array.Empty<IEventPipelineBehavior>());
        _transportDispatcher.DispatchAsync(@event, Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);

        // Act (When)
        var result = await dispatcher.DispatchAsync(@event, DistributionMode.External, CancellationToken.None);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        await _transportDispatcher.Received(1).DispatchAsync(@event, Arg.Any<CancellationToken>());
        await _eventOrchestrator.DidNotReceive().RunAsync(
            Arg.Any<IEnumerable<IEventHandler<EventExample>>>(),
            Arg.Any<EventExample>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchAsync_WithHybridMode_ExecutesBothLocalAndExternal()
    {
        // Arrange (Given)
        var @event = new EventExample("Test Event");
        var handler = new EventExampleHandler1();
        var dispatcher = CreateDispatcher();

        _dependencyResolver.GetServices<IEventHandler<EventExample>>()
            .Returns(new[] { handler });
        _dependencyResolver.GetServices<IEventPipelineBehavior>()
            .Returns(Array.Empty<IEventPipelineBehavior>());
        _eventOrchestrator.RunAsync(Arg.Any<IEventHandler<EventExample>[]>(), @event, Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _transportDispatcher.DispatchAsync(@event, Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);

        // Act (When)
        var result = await dispatcher.DispatchAsync(
            @event,
            DistributionMode.Local | DistributionMode.External,
            CancellationToken.None);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        await _eventOrchestrator.Received(1).RunAsync(
            Arg.Is<IEventHandler<EventExample>[]>(h => h.Length == 1),
            @event,
            Arg.Any<CancellationToken>());
        await _transportDispatcher.Received(1).DispatchAsync(@event, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchAsync_WithUndefinedMode_UsesDefaultDistributionMode()
    {
        // Arrange (Given)
        var @event = new EventExample("Test Event");
        var dispatcher = CreateDispatcher();

        _dependencyResolver.GetServices<IEventHandler<EventExample>>()
            .Returns(Array.Empty<IEventHandler<EventExample>>());
        _dependencyResolver.GetServices<IEventPipelineBehavior>()
            .Returns(Array.Empty<IEventPipelineBehavior>());
        _eventOrchestrator.RunAsync(Arg.Any<IEventHandler<EventExample>[]>(), @event, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        // Act (When)
        var result = await dispatcher.DispatchAsync(@event, DistributionMode.Undefined, CancellationToken.None);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        await _eventOrchestrator.Received(1).RunAsync(
            Arg.Any<IEventHandler<EventExample>[]>(),
            @event,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchAsync_WithPublishEventTrait_UsesTraitDistributionMode()
    {
        // Arrange (Given)
        var @event = new EventExample("Test Event");
        var trait = Substitute.For<IPublishEventTrait>();
        trait.EventType.Returns(typeof(EventExample));
        trait.DistributionMode.Returns(DistributionMode.External);

        var dispatcher = CreateDispatcher(publishEventTraits: new[] { trait });

        _dependencyResolver.GetServices<IEventHandler<EventExample>>()
            .Returns(Array.Empty<IEventHandler<EventExample>>());
        _dependencyResolver.GetServices<IEventPipelineBehavior>()
            .Returns(Array.Empty<IEventPipelineBehavior>());
        _transportDispatcher.DispatchAsync(@event, Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);

        // Act (When)
        var result = await dispatcher.DispatchAsync(@event, DistributionMode.Undefined, CancellationToken.None);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        await _transportDispatcher.Received(1).DispatchAsync(@event, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchAsync_WithRoutingFilter_UsesFilterDistributionMode()
    {
        // Arrange (Given)
        var @event = new EventExample("Test Event");
        var filter = Substitute.For<IEventRoutingFilter>();
        filter.Order.Returns(0);
        filter.GetDistributionMode(@event).Returns(DistributionMode.External);

        var dispatcher = CreateDispatcher(routingFilters: new[] { filter });

        _dependencyResolver.GetServices<IEventHandler<EventExample>>()
            .Returns(Array.Empty<IEventHandler<EventExample>>());
        _dependencyResolver.GetServices<IEventPipelineBehavior>()
            .Returns(Array.Empty<IEventPipelineBehavior>());
        _transportDispatcher.DispatchAsync(@event, Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);

        // Act (When)
        var result = await dispatcher.DispatchAsync(@event, DistributionMode.Undefined, CancellationToken.None);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        await _transportDispatcher.Received(1).DispatchAsync(@event, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchAsync_WithMultipleRoutingFilters_UsesFirstMatchingFilter()
    {
        // Arrange (Given)
        var @event = new EventExample("Test Event");

        var filter1 = Substitute.For<IEventRoutingFilter>();
        filter1.Order.Returns(0);
        filter1.GetDistributionMode(@event).Returns((DistributionMode?)null);

        var filter2 = Substitute.For<IEventRoutingFilter>();
        filter2.Order.Returns(1);
        filter2.GetDistributionMode(@event).Returns(DistributionMode.External);

        var filter3 = Substitute.For<IEventRoutingFilter>();
        filter3.Order.Returns(2);
        filter3.GetDistributionMode(@event).Returns(DistributionMode.Local);

        var dispatcher = CreateDispatcher(routingFilters: new[] { filter1, filter2, filter3 });

        _dependencyResolver.GetServices<IEventHandler<EventExample>>()
            .Returns(Array.Empty<IEventHandler<EventExample>>());
        _dependencyResolver.GetServices<IEventPipelineBehavior>()
            .Returns(Array.Empty<IEventPipelineBehavior>());
        _transportDispatcher.DispatchAsync(@event, Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);

        // Act (When)
        var result = await dispatcher.DispatchAsync(@event, DistributionMode.Undefined, CancellationToken.None);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        await _transportDispatcher.Received(1).DispatchAsync(@event, Arg.Any<CancellationToken>());
        filter3.DidNotReceive().GetDistributionMode(Arg.Any<IEvent>());
    }

    [Fact]
    public async Task DispatchAsync_WithTraitAndFilter_PrefersTraitOverFilter()
    {
        // Arrange (Given)
        var @event = new EventExample("Test Event");

        var trait = Substitute.For<IPublishEventTrait>();
        trait.EventType.Returns(typeof(EventExample));
        trait.DistributionMode.Returns(DistributionMode.Local);

        var filter = Substitute.For<IEventRoutingFilter>();
        filter.Order.Returns(0);
        filter.GetDistributionMode(@event).Returns(DistributionMode.External);

        var dispatcher = CreateDispatcher(
            routingFilters: new[] { filter },
            publishEventTraits: new[] { trait });

        _dependencyResolver.GetServices<IEventHandler<EventExample>>()
            .Returns(Array.Empty<IEventHandler<EventExample>>());
        _dependencyResolver.GetServices<IEventPipelineBehavior>()
            .Returns(Array.Empty<IEventPipelineBehavior>());
        _eventOrchestrator.RunAsync(Arg.Any<IEventHandler<EventExample>[]>(), @event, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        // Act (When)
        var result = await dispatcher.DispatchAsync(@event, DistributionMode.Undefined, CancellationToken.None);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        await _eventOrchestrator.Received(1).RunAsync(
            Arg.Any<IEventHandler<EventExample>[]>(),
            @event,
            Arg.Any<CancellationToken>());
        filter.DidNotReceive().GetDistributionMode(Arg.Any<IEvent>());
    }

    [Fact]
    public async Task DispatchAsync_WithPipelineBehaviors_ExecutesBehaviorsInOrder()
    {
        // Arrange (Given)
        var @event = new EventExample("Test Event");
        var behavior1 = new TestEventPipelineBehavior();
        var behavior2 = new TestEventPipelineBehavior();
        var dispatcher = CreateDispatcher();

        _dependencyResolver.GetServices<IEventHandler<EventExample>>()
            .Returns(Array.Empty<IEventHandler<EventExample>>());
        _dependencyResolver.GetServices<IEventPipelineBehavior>()
            .Returns(new IEventPipelineBehavior[] { behavior1, behavior2 });
        _eventOrchestrator.RunAsync(Arg.Any<IEventHandler<EventExample>[]>(), @event, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        // Act (When)
        var result = await dispatcher.DispatchAsync(@event, DistributionMode.Local, CancellationToken.None);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        Assert.True(behavior1.Executed);
        Assert.True(behavior2.Executed);
        Assert.Equal(1, behavior1.ExecutionCount);
        Assert.Equal(1, behavior2.ExecutionCount);
    }

    [Fact]
    public async Task DispatchAsync_WithNoHandlers_CompletesSuccessfully()
    {
        // Arrange (Given)
        var @event = new EventExample("Test Event");
        var dispatcher = CreateDispatcher();

        _dependencyResolver.GetServices<IEventHandler<EventExample>>()
            .Returns(Array.Empty<IEventHandler<EventExample>>());
        _dependencyResolver.GetServices<IEventPipelineBehavior>()
            .Returns(Array.Empty<IEventPipelineBehavior>());
        _eventOrchestrator.RunAsync(Arg.Any<IEventHandler<EventExample>[]>(), @event, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        // Act (When)
        var result = await dispatcher.DispatchAsync(@event, DistributionMode.Local, CancellationToken.None);

        // Assert (Then)
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task DispatchAsync_WithMultipleHandlers_ExecutesAllHandlers()
    {
        // Arrange (Given)
        var @event = new EventExample("Test Event");
        var handler1 = new EventExampleHandler1();
        var handler2 = new EventExampleHandler1();
        var dispatcher = CreateDispatcher();

        _dependencyResolver.GetServices<IEventHandler<EventExample>>()
            .Returns(new[] { handler1, handler2 });
        _dependencyResolver.GetServices<IEventPipelineBehavior>()
            .Returns(Array.Empty<IEventPipelineBehavior>());
        _eventOrchestrator.RunAsync(Arg.Any<IEventHandler<EventExample>[]>(), @event, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        // Act (When)
        var result = await dispatcher.DispatchAsync(@event, DistributionMode.Local, CancellationToken.None);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        await _eventOrchestrator.Received(1).RunAsync(
            Arg.Is<IEventHandler<EventExample>[]>(h => h.Length == 2),
            @event,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchAsync_RecordsMetrics()
    {
        // Arrange (Given)
        var @event = new EventExample("Test Event");
        var dispatcher = CreateDispatcher();

        _dependencyResolver.GetServices<IEventHandler<EventExample>>()
            .Returns(Array.Empty<IEventHandler<EventExample>>());
        _dependencyResolver.GetServices<IEventPipelineBehavior>()
            .Returns(Array.Empty<IEventPipelineBehavior>());
        _eventOrchestrator.RunAsync(Arg.Any<IEventHandler<EventExample>[]>(), @event, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        // Act (When)
        await dispatcher.DispatchAsync(@event, DistributionMode.Local, CancellationToken.None);

        // Assert (Then)
        _metrics.Received(1).RecordDispatchLatency(
            Arg.Any<double>(),
            "EventExample",
            "Local");
        _metrics.Received(1).RecordEventDispatched(
            "EventExample",
            "Local",
            true);
    }

    [Fact]
    public async Task DispatchAsync_OnFailure_RecordsFailureMetrics()
    {
        // Arrange (Given)
        var @event = new EventExample("Test Event");
        var dispatcher = CreateDispatcher();

        _dependencyResolver.GetServices<IEventHandler<EventExample>>()
            .Returns(Array.Empty<IEventHandler<EventExample>>());
        _dependencyResolver.GetServices<IEventPipelineBehavior>()
            .Returns(Array.Empty<IEventPipelineBehavior>());
        _eventOrchestrator.RunAsync(Arg.Any<IEventHandler<EventExample>[]>(), @event, Arg.Any<CancellationToken>())
            .Returns(Result.Failure("Test failure"));

        // Act (When)
        var result = await dispatcher.DispatchAsync(@event, DistributionMode.Local, CancellationToken.None);

        // Assert (Then)
        Assert.False(result.IsSuccess);
        _metrics.Received(1).RecordEventDispatched(
            "EventExample",
            "Local",
            false);
    }

    [Fact]
    public async Task DispatchAsync_CachesDistributionModeDecision()
    {
        // Arrange (Given)
        var event1 = new EventExample("Event 1");
        var event2 = new EventExample("Event 2");
        var filter = Substitute.For<IEventRoutingFilter>();
        filter.Order.Returns(0);
        filter.GetDistributionMode(Arg.Any<EventExample>()).Returns(DistributionMode.External);

        var dispatcher = CreateDispatcher(routingFilters: new[] { filter });

        _dependencyResolver.GetServices<IEventHandler<EventExample>>()
            .Returns(Array.Empty<IEventHandler<EventExample>>());
        _dependencyResolver.GetServices<IEventPipelineBehavior>()
            .Returns(Array.Empty<IEventPipelineBehavior>());
        _transportDispatcher.DispatchAsync(Arg.Any<EventExample>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);

        // Act (When)
        await dispatcher.DispatchAsync(event1, DistributionMode.Undefined, CancellationToken.None);
        await dispatcher.DispatchAsync(event2, DistributionMode.Undefined, CancellationToken.None);

        // Assert (Then)
        filter.Received(1).GetDistributionMode(Arg.Any<IEvent>());
    }

    [Fact]
    public async Task DispatchAsync_WithExplicitMode_BypassesDistributionModeResolution()
    {
        // Arrange (Given)
        var @event = new EventExample("Test Event");
        var filter = Substitute.For<IEventRoutingFilter>();
        filter.Order.Returns(0);

        var dispatcher = CreateDispatcher(routingFilters: new[] { filter });

        _dependencyResolver.GetServices<IEventHandler<EventExample>>()
            .Returns(Array.Empty<IEventHandler<EventExample>>());
        _dependencyResolver.GetServices<IEventPipelineBehavior>()
            .Returns(Array.Empty<IEventPipelineBehavior>());
        _eventOrchestrator.RunAsync(Arg.Any<IEventHandler<EventExample>[]>(), @event, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        // Act (When)
        await dispatcher.DispatchAsync(@event, DistributionMode.Local, CancellationToken.None);

        // Assert (Then)
        filter.DidNotReceive().GetDistributionMode(Arg.Any<IEvent>());
    }

    [Fact]
    public async Task DispatchAsync_WithInheritedEvent_ExecutesHandlerForConcreteType()
    {
        // Arrange (Given)
        BaseEventExample @event = new InheritedEventExample("Test Event");
        var handler = new InheritedEventExampleHandler();

        // Register dispatcher delegate for the concrete type (mimics NativeAOT-compatible registration)
        var options = new EventDispatcherOptions
        {
            DefaultDistributionMode = DistributionMode.Local,
            Dispatchers = new Dictionary<Type, DispatchEventDelegate>
            {
                [typeof(InheritedEventExample)] = async (e, d, ct) =>
                {
                    var typedEvent = (InheritedEventExample)e;
                    return await d.DispatchAsync(typedEvent, DistributionMode.Local, ct);
                }
            }
        };

        var dispatcher = CreateDispatcher(options: options);

        _dependencyResolver.GetServices<IEventHandler<InheritedEventExample>>()
            .Returns(new[] { handler });
        _dependencyResolver.GetServices<IEventPipelineBehavior>()
            .Returns(Array.Empty<IEventPipelineBehavior>());
        _eventOrchestrator.RunAsync(
                Arg.Any<IEventHandler<InheritedEventExample>[]>(),
                Arg.Any<InheritedEventExample>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var handlers = callInfo.ArgAt<IEventHandler<InheritedEventExample>[]>(0);
                var evt = callInfo.ArgAt<InheritedEventExample>(1);
                var ct = callInfo.ArgAt<CancellationToken>(2);
                return handlers[0].HandleAsync(evt, ct);
            });

        // Act (When)
        await dispatcher.DispatchAsync(@event, DistributionMode.Local, CancellationToken.None);

        // Assert (Then)
        Assert.True(handler.Executed);
        Assert.Equal(@event, handler.EventExecuted);
        Assert.Equal(1, handler.ExecutionCount);
    }
}
