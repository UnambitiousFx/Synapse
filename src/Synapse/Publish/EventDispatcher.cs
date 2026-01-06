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

/// <summary>
///     Functional component responsible for all event dispatching logic.
///     Coordinates distribution mode determination, pipeline execution, and outbox integration.
/// </summary>
/// <remarks>
///     <para>
///         The EventDispatcher is the central orchestrator for event processing in the unified event dispatching system.
///         It handles:
///         <list type="bullet">
///             <item>Distribution mode determination through routing filters, message traits, and default configuration</item>
///             <item>Pipeline behavior execution for cross-cutting concerns</item>
///             <item>Integration with the outbox pattern for reliable event delivery</item>
///             <item>OpenTelemetry tracing and metrics collection</item>
///             <item>NativeAOT-compatible event replay from outbox</item>
///         </list>
///     </para>
///     <para>
///         <b>Distribution Mode Resolution Order:</b>
///         <list type="number">
///             <item>Routing filters (evaluated in order of <see cref="IEventRoutingFilter.Order" />)</item>
///             <item>Default distribution mode from <see cref="EventDispatcherOptions.DefaultDistributionMode" /></item>
///         </list>
///     </para>
///     <para>
///         <b>NativeAOT Compatibility:</b>
///         The EventDispatcher uses pre-registered dispatcher delegates to avoid reflection when replaying events
///         from the outbox. These delegates are registered at startup via source generation or explicit registration.
///     </para>
/// </remarks>
internal sealed class EventDispatcher : IEventDispatcher
{
    private readonly IDependencyResolver _dependencyResolver;
    private readonly IEventOrchestrator _eventOrchestrator;
    private readonly ILogger<EventDispatcher> _logger;
    private readonly ISynapseMetrics _metrics;
    private readonly EventDispatcherOptions _options;
    private readonly Dictionary<Type, IPublishEventTrait> _publishEventTraits;

    // Cache for routing decisions per event type
    private readonly ConcurrentDictionary<Type, DistributionMode> _routingCache = new();
    private readonly IEventRoutingFilter[] _routingFilters;
    private readonly ITransportDispatcher _transportDispatcher;

    public EventDispatcher(
        IDependencyResolver dependencyResolver,
        IEnumerable<IEventRoutingFilter> routingFilters,
        IEnumerable<IPublishEventTrait> publishEventTraits,
        ISynapseMetrics metrics,
        IEventOrchestrator eventOrchestrator,
        ITransportDispatcher transportDispatcher,
        IOptions<EventDispatcherOptions> options,
        ILogger<EventDispatcher> logger)
    {
        _dependencyResolver = dependencyResolver;
        _publishEventTraits = publishEventTraits.ToDictionary(x => x.EventType);
        _routingFilters = routingFilters.OrderBy(f => f.Order).ToArray();
        _options = options.Value;
        _logger = logger;
        _metrics = metrics;
        _eventOrchestrator = eventOrchestrator;
        _transportDispatcher = transportDispatcher;
    }

    public ValueTask<Result> DispatchAsync<TEvent>(TEvent @event,
        DistributionMode distributionMode,
        CancellationToken cancellationToken)
        where TEvent : class, IEvent
    {
        // 1. Determine distribution mode
        if (distributionMode == DistributionMode.Undefined) distributionMode = DetermineDistributionMode(@event);

        // 2. Get handlers and behaviors
        var handlers = _dependencyResolver.GetServices<IEventHandler<TEvent>>();
        var behaviors = _dependencyResolver.GetServices<IEventPipelineBehavior>();

        return ExecutePipelineAsync(
            @event,
            handlers.ToArray(),
            behaviors.ToArray(),
            distributionMode,
            0,
            cancellationToken);
    }

    private DistributionMode DetermineDistributionMode<TEvent>(TEvent @event)
        where TEvent : class, IEvent
    {
        var eventType = typeof(TEvent);

        // Check cache first for deterministic routing
        if (_routingCache.TryGetValue(eventType, out var cachedMode))
        {
            _logger.LogDebug("Using cached distribution mode {DistributionMode} for event type {EventType}",
                cachedMode, eventType.Name);
            return cachedMode;
        }

        _logger.LogDebug("Determining distribution mode for event type {EventType}", eventType.Name);

        // 1. Find specific traits for this event type
        if (_publishEventTraits.TryGetValue(eventType, out var trait))
        {
            _logger.LogDebug("Found message traits for event type {EventType}: {Traits}", eventType.Name, trait);
            return trait.DistributionMode;
        }

        // 2. Evaluate routing filters in order
        if (_routingFilters.Length > 0)
        {
            _logger.LogDebug("Evaluating {FilterCount} routing filters for event type {EventType}",
                _routingFilters.Length, eventType.Name);

            foreach (var filter in _routingFilters)
            {
                var filterType = filter.GetType().Name;
                _logger.LogTrace("Evaluating routing filter {FilterType} (Order: {Order}) for event type {EventType}",
                    filterType, filter.Order, eventType.Name);

                var mode = filter.GetDistributionMode(@event);
                if (mode.HasValue)
                {
                    _logger.LogInformation(
                        "Routing filter {FilterType} determined distribution mode {DistributionMode} for event type {EventType}",
                        filterType, mode.Value, eventType.Name);

                    // Cache the decision (filters should be deterministic for caching to be safe)
                    _routingCache.TryAdd(eventType, mode.Value);
                    return mode.Value;
                }

                _logger.LogTrace("Routing filter {FilterType} returned no distribution mode for event type {EventType}",
                    filterType, eventType.Name);
            }

            _logger.LogDebug(
                "No routing filter determined distribution mode for event type {EventType}, falling back to message traits",
                eventType.Name);
        }

        // 3. Use default from options
        _logger.LogDebug(
            "Using default distribution mode {DistributionMode} for event type {EventType}",
            _options.DefaultDistributionMode, eventType.Name);

        // Cache the decision
        _routingCache.TryAdd(eventType, _options.DefaultDistributionMode);
        return _options.DefaultDistributionMode;
    }

    private ValueTask<Result> ExecutePipelineAsync<TEvent>(
        TEvent @event,
        IEventHandler<TEvent>[] handlers,
        IEventPipelineBehavior[] behaviors,
        DistributionMode distributionMode,
        int index,
        CancellationToken cancellationToken)
        where TEvent : class, IEvent
    {
        if (index >= behaviors.Length)
            return DispatchByModeAsync(@event, handlers, distributionMode, cancellationToken);

        return behaviors[index].HandleAsync(@event, Next, cancellationToken);

        ValueTask<Result> Next()
        {
            return ExecutePipelineAsync(@event, handlers, behaviors, distributionMode, index + 1,
                cancellationToken);
        }
    }

    private async ValueTask<Result> DispatchByModeAsync<TEvent>(
        TEvent @event,
        IEventHandler<TEvent>[] handlers,
        DistributionMode distributionMode,
        CancellationToken cancellationToken)
        where TEvent : class, IEvent
    {
        var eventType = typeof(TEvent).Name;
        var distributionModeStr = distributionMode.ToString();
        var startTime = Stopwatch.GetTimestamp();

        using var activity = SynapseActivitySource.Source.StartActivity(
            "mediator.event.dispatch",
            ActivityKind.Producer);

        activity?.SetTag("messaging.distribution_mode", distributionModeStr);
        activity?.SetTag("messaging.event_type", eventType);

        try
        {
            var result = await ExecuteDirectAsync(
                @event,
                handlers,
                distributionMode,
                cancellationToken);


            // Record metrics
            var elapsedMs = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
            _metrics.RecordDispatchLatency(elapsedMs, eventType, distributionModeStr);
            _metrics.RecordEventDispatched(eventType, distributionModeStr, result.IsSuccess);

            return result;
        }
        catch (Exception ex)
        {
            // Record failure metrics
            var elapsedMs = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
            _metrics.RecordDispatchLatency(elapsedMs, eventType, distributionModeStr);
            _metrics.RecordEventDispatched(eventType, distributionModeStr, false);

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Error dispatching event {EventType} with distribution mode {DistributionMode}",
                eventType, distributionMode);
            throw;
        }
    }

    private async ValueTask<Result> ExecuteDirectAsync<TEvent>(
        TEvent @event,
        IEventHandler<TEvent>[] handlers,
        DistributionMode distributionMode,
        CancellationToken cancellationToken)
        where TEvent : class, IEvent
    {
        if (distributionMode.HasFlag(DistributionMode.External) && distributionMode.HasFlag(DistributionMode.Local))
            return await ExecuteHybridAsync(@event, handlers, cancellationToken);

        if (distributionMode.HasFlag(DistributionMode.Local))
            return await ExecuteLocalAsync(@event, handlers, cancellationToken);

        if (distributionMode.HasFlag(DistributionMode.External))
            return await ExecuteExternalAsync(@event, cancellationToken);

        return Result.Failure($"Unknown distribution mode: {distributionMode}");
    }

    private ValueTask<Result> ExecuteLocalAsync<TEvent>(
        TEvent @event,
        IEventHandler<TEvent>[] handlers,
        CancellationToken cancellationToken)
        where TEvent : class, IEvent
    {
        var eventType = typeof(TEvent).Name;
        _logger.LogDebug(
            "Executing local handlers for event {EventType} ({HandlerCount} handlers)",
            eventType, handlers.Length);

        return _eventOrchestrator.RunAsync(handlers, @event, cancellationToken);
    }

    private async ValueTask<Result> ExecuteHybridAsync<TEvent>(
        TEvent @event,
        IEventHandler<TEvent>[] handlers,
        CancellationToken cancellationToken)
        where TEvent : class, IEvent
    {
        var eventType = typeof(TEvent).Name;
        _logger.LogDebug(
            "Executing hybrid dispatch for event {EventType} (local handlers: {HandlerCount})",
            eventType, handlers.Length);

        // Execute local and external in parallel
        var localTask = ExecuteLocalAsync(@event, handlers, cancellationToken).AsTask();
        var externalTask = ExecuteExternalAsync(@event, cancellationToken).AsTask();

        await Task.WhenAll(localTask, externalTask);

        var localResult = await localTask;
        var externalResult = await externalTask;

        var combinedResult = new[] { localResult, externalResult }.Combine();

        if (combinedResult.IsSuccess)
            _logger.LogDebug(
                "Hybrid dispatch completed successfully for event {EventType}",
                eventType);
        else
            _logger.LogWarning(
                "Hybrid dispatch completed with failures for event {EventType}: {Error}",
                eventType, combinedResult.ToString());

        return combinedResult;
    }

    private async ValueTask<Result> ExecuteExternalAsync<TEvent>(
        TEvent @event,
        CancellationToken cancellationToken)
        where TEvent : class, IEvent
    {
        var eventType = typeof(TEvent).Name;
        _logger.LogDebug(
            "Dispatching event {EventType} to external transport",
            eventType);

        await _transportDispatcher.DispatchAsync(@event, cancellationToken);

        _logger.LogDebug(
            "Event {EventType} dispatched to external transport successfully",
            eventType);

        return Result.Success();
    }
}