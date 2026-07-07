using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Observability;
using UnambitiousFx.Synapse.Publish.Orchestrators;
using UnambitiousFx.Synapse.Resolvers;

namespace UnambitiousFx.Synapse.Publish;

internal sealed class EventDispatcher : IEventDispatcher
{
    private readonly IDependencyResolver _dependencyResolver;
    private readonly IEventOrchestrator _eventOrchestrator;
    private readonly ILogger<EventDispatcher> _logger;
    private readonly ISynapseMetrics _metrics;
    private readonly EventDispatcherOptions _options;

    // The fully-composed behavior chain (an EventHandlerDelegate&lt;TEvent&gt;) cached per event type for the
    // dispatcher's (scoped) lifetime. The sort and the nested-delegate composition are paid once per type
    // per scope; each subsequent publish just invokes the cached delegate with no per-dispatch closure
    // allocation — matching how the request proxies compose once in their constructor. ConcurrentDictionary
    // guards the case where a single scope issues concurrent dispatches (e.g. Task.WhenAll over publishes).
    private readonly ConcurrentDictionary<Type, object> _pipelineCache = new();


    public EventDispatcher(
        IDependencyResolver dependencyResolver,
        ISynapseMetrics metrics,
        IEventOrchestrator eventOrchestrator,
        IOptions<EventDispatcherOptions> options,
        ILogger<EventDispatcher> logger)
    {
        _dependencyResolver = dependencyResolver;
        _options = options.Value;
        _logger = logger;
        _metrics = metrics;
        _eventOrchestrator = eventOrchestrator;
    }

    public ValueTask<Result> DispatchAsync<TEvent>(TEvent @event,
        CancellationToken cancellationToken)
        where TEvent : class, IEvent
    {
        var runtimeType = @event.GetType();
        var genericType = typeof(TEvent);

        // Handle polymorphic dispatch: if runtime type differs from generic type,
        if (runtimeType != genericType)
        {
            _logger.LogDebug(
                "Runtime type {RuntimeType} differs from generic type {GenericType}, using dispatcher delegate",
                runtimeType.Name, genericType.Name);

            var dispatcher = _options.Dispatchers.GetValueOrDefault(runtimeType);
            if (dispatcher == null)
            {
                _logger.LogError(
                    "No dispatcher registered for runtime event type {RuntimeType}",
                    runtimeType.Name);
                return new ValueTask<Result>(
                    Result.Failure($"No dispatcher registered for event type {runtimeType.Name}"));
            }

            // Delegate contains correct generic type via closure, maintaining NativeAOT compatibility
            return dispatcher(@event, this, cancellationToken);
        }


        // Resolve handlers + behaviors and compose the chain once per event type (cached). The static
        // factory captures no state (only the type parameter), so on a cache hit no closure is allocated.
        var pipeline = (EventHandlerDelegate<TEvent>)_pipelineCache.GetOrAdd(
            genericType,
            static (_, state) => state.BuildPipeline<TEvent>(),
            this);

        return pipeline(@event, cancellationToken);
    }

    /// <summary>
    ///     Composes the behavior chain for <typeparamref name="TEvent" /> into a single delegate: the
    ///     terminal fans the event out to all handlers, and each behavior (lowest <c>Order</c> outermost)
    ///     wraps the next. Built once per type per scope; the resolved handler/behavior instances are
    ///     captured for the dispatcher's (scoped) lifetime, consistent with scoped-service stability.
    /// </summary>
    private EventHandlerDelegate<TEvent> BuildPipeline<TEvent>()
        where TEvent : class, IEvent
    {
        // Behaviors are resolved as IEventPipelineBehavior<TEvent> so only behaviors declared for this
        // exact event type are returned — no runtime type-filtering needed.
        var handlers = _dependencyResolver.GetServices<IEventHandler<TEvent>>() as IEventHandler<TEvent>[] ??
                       _dependencyResolver.GetServices<IEventHandler<TEvent>>().ToArray();
        // Ordered by runtime pipeline position (IOrderedPipelineBehavior); the stable sort keeps
        // registration order for behaviors that share an Order.
        var behaviors = _dependencyResolver.GetServices<IEventPipelineBehavior<TEvent>>()
            .OrderBy(Pipelines.PipelineBehaviorOrdering.OrderOf)
            .ToArray();

        EventHandlerDelegate<TEvent> next = (e, ct) => DispatchToHandlersAsync(e, handlers, ct);
        for (var i = behaviors.Length - 1; i >= 0; i--)
        {
            var behavior = behaviors[i];
            var captured = next;
            next = (e, ct) => behavior.HandleAsync(e, captured, ct);
        }

        return next;
    }

    private async ValueTask<Result> DispatchToHandlersAsync<TEvent>(
        TEvent @event,
        IEventHandler<TEvent>[] handlers,
        CancellationToken cancellationToken)
        where TEvent : class, IEvent
    {
        var eventType = typeof(TEvent).Name;
        var startTime = Stopwatch.GetTimestamp();

        using var activity = SynapseActivitySource.Source.StartActivity(
            "synapse.mediator.event.dispatch",
            ActivityKind.Producer);

        activity?.SetTag("synapse.mediator.event_type", eventType);

        try
        {
            _logger.LogDebug(
                "Dispatching event {EventType} to {HandlerCount} handler(s)",
                eventType, handlers.Length);

            var result = await _eventOrchestrator.RunAsync(handlers, @event, cancellationToken);

            // Record metrics
            var elapsedMs = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
            _metrics.RecordDispatchLatency(elapsedMs, eventType);
            _metrics.RecordEventDispatched(eventType, result.IsSuccess);

            return result;
        }
        catch (Exception ex)
        {
            // Record failure metrics
            var elapsedMs = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
            _metrics.RecordDispatchLatency(elapsedMs, eventType);
            _metrics.RecordEventDispatched(eventType, false);

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Error dispatching event {EventType}", eventType);
            throw;
        }
    }
}