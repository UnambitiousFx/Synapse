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

    // Sorted event behaviors cached per event type for the dispatcher's (scoped) lifetime, so the
    // OrderBy/ToArray cost is paid once per type per scope rather than on every dispatch — matching
    // how the request/stream proxies sort once in their constructor. ConcurrentDictionary guards the
    // case where a single scope issues concurrent dispatches (e.g. Task.WhenAll over publishes).
    private readonly ConcurrentDictionary<Type, object> _behaviorCache = new();


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


        // Get handlers and behaviors as arrays to avoid repeated enumeration.
        // Behaviors are resolved as IEventPipelineBehavior<TEvent> so only behaviors declared
        // for this exact event type are returned — no runtime type-filtering needed.
        var handlersArray = _dependencyResolver.GetServices<IEventHandler<TEvent>>() as IEventHandler<TEvent>[] ??
                            _dependencyResolver.GetServices<IEventHandler<TEvent>>().ToArray();
        // Behaviors are ordered by their runtime pipeline position (IOrderedPipelineBehavior); the
        // stable sort keeps registration order for behaviors that share an Order. The resolved and
        // sorted array is cached per event type so the sort runs once per type within the scope.
        var behaviorsArray = (IEventPipelineBehavior<TEvent>[])_behaviorCache.GetOrAdd(
            genericType,
            static (_, resolver) => resolver
                .GetServices<IEventPipelineBehavior<TEvent>>()
                .OrderBy(Pipelines.PipelineBehaviorOrdering.OrderOf)
                .ToArray(),
            _dependencyResolver);

        return ExecutePipelineAsync(
            @event,
            handlersArray,
            behaviorsArray,
            0,
            cancellationToken);
    }


    private ValueTask<Result> ExecutePipelineAsync<TEvent>(
        TEvent @event,
        IEventHandler<TEvent>[] handlers,
        IEventPipelineBehavior<TEvent>[] behaviors,
        int index,
        CancellationToken cancellationToken)
        where TEvent : class, IEvent
    {
        if (index >= behaviors.Length)
        {
            return DispatchToHandlersAsync(@event, handlers, cancellationToken);
        }

        return behaviors[index].HandleAsync(@event, Next, cancellationToken);

        ValueTask<Result> Next(TEvent inEvent, CancellationToken inCancellationToken)
        {
            return ExecutePipelineAsync(inEvent, handlers, behaviors, index + 1,
                inCancellationToken);
        }
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