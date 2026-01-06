using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Observability;

namespace UnambitiousFx.Synapse.Publish.Outbox;

/// <summary>
///     Manages outbox storage and dispatch strategies for both local and external events.
///     Coordinates event storage, retry logic, and distribution mode-based dispatch.
/// </summary>
internal sealed class OutboxManager : IOutboxManager
{
    private readonly IEventDispatcher _eventDispatcher;
    private readonly ILogger<OutboxManager> _logger;
    private readonly ISynapseMetrics _metrics;
    private readonly EventDispatcherOptions _options;
    private readonly OutboxOptions _outboxOptions;
    private readonly IEventOutboxStorage _outboxStorage;

    public OutboxManager(
        IEventOutboxStorage outboxStorage,
        IEventDispatcher eventDispatcher,
        ISynapseMetrics metrics,
        IOptions<EventDispatcherOptions> options,
        IOptions<OutboxOptions> outboxOptions,
        ILogger<OutboxManager> logger)
    {
        _outboxStorage = outboxStorage;
        _eventDispatcher = eventDispatcher;
        _options = options.Value;
        _outboxOptions = outboxOptions.Value;
        _logger = logger;
        _metrics = metrics;
    }

    public async ValueTask<Result> ProcessPendingAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Processing pending events from outbox");

        var pendingEvents = await _outboxStorage.GetPendingEventsAsync(cancellationToken);
        var events = _outboxOptions.BatchSize.HasValue
            ? pendingEvents.Take(_outboxOptions.BatchSize.Value).ToList()
            : pendingEvents.ToList();

        if (events.Count == 0)
        {
            _logger.LogDebug("No pending events found in outbox");
            return Result.Success();
        }

        _logger.LogInformation(
            "Processing {EventCount} pending events from outbox (batch size: {BatchSize})",
            events.Count, _outboxOptions.BatchSize);

        var results = new List<Result>();

        foreach (var @event in events)
        {
            var result = await DispatchEventAsync(@event, cancellationToken);
            results.Add(result);
        }

        var combinedResult = results.Combine();

        if (combinedResult.IsSuccess)
            _logger.LogInformation(
                "Successfully processed {EventCount} pending events from outbox",
                events.Count);
        else
            _logger.LogWarning(
                "Completed processing {EventCount} pending events from outbox with failures: {Error}",
                events.Count, combinedResult.ToString());

        return combinedResult;
    }

    public ValueTask<Result> StoreAsync<TEvent>(TEvent @event,
        DistributionMode distributionMode,
        CancellationToken cancellationToken)
        where TEvent : class, IEvent
    {
        return _outboxStorage.AddAsync(@event, distributionMode, cancellationToken);
    }


    private async ValueTask<Result> DispatchEventAsync(
        IEvent @event,
        CancellationToken cancellationToken)
    {
        var eventType = @event.GetType().Name;

        try
        {
            var distributionMode = await _outboxStorage.GetDistributionModeAsync(@event, cancellationToken);

            _logger.LogDebug(
                "Dispatching event {EventType} from outbox with distribution mode {DistributionMode}",
                eventType, distributionMode);

            // Use the registered dispatcher delegate to maintain type information
            // This delegate is registered at startup via source generation or explicit registration
            var dispatcher = _options.Dispatchers.GetValueOrDefault(@event.GetType());
            if (dispatcher == null)
            {
                _logger.LogError(
                    "No dispatcher registered for event type {EventType}, cannot process from outbox",
                    eventType);
                return Result.Failure($"No dispatcher registered for event type {eventType}");
            }

            // The dispatcher delegate calls EventDispatcher.DispatchFromOutboxAsync<TEvent>
            // with the correct generic type, avoiding reflection
            var result = await dispatcher(@event, _eventDispatcher, cancellationToken);

            if (result.IsSuccess)
            {
                _logger.LogDebug(
                    "Event {EventType} dispatched successfully from outbox, marking as processed",
                    eventType);
                await _outboxStorage.MarkAsProcessedAsync(@event, cancellationToken);
                _metrics.RecordOutboxEventProcessed(eventType, true);
            }
            else
            {
                _logger.LogWarning(
                    "Event {EventType} dispatch from outbox failed: {Error}",
                    eventType, result.ToString());
                await HandleDispatchFailureAsync(@event, result.ToString()!, cancellationToken);
                _metrics.RecordOutboxEventProcessed(eventType, false);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Exception occurred while dispatching event {EventType} from outbox",
                eventType);
            await HandleDispatchFailureAsync(@event, ex.Message, cancellationToken);
            return Result.Failure(ex.Message);
        }
    }

    /// <summary>
    ///     Handles dispatch failures by calculating retry delays and moving events to dead-letter when appropriate.
    /// </summary>
    /// <param name="event">The event that failed to dispatch.</param>
    /// <param name="reason">The reason for the failure.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async ValueTask HandleDispatchFailureAsync(
        IEvent @event,
        string reason,
        CancellationToken cancellationToken)
    {
        var eventType = @event.GetType().Name;
        var attemptCount = await _outboxStorage.GetAttemptCountAsync(@event, cancellationToken) ?? 0;
        var nextAttemptNumber = attemptCount + 1;
        var shouldDeadLetter = nextAttemptNumber >= _outboxOptions.MaxRetryAttempts;

        DateTimeOffset? nextAttemptAt = null;
        TimeSpan? calculatedDelay;

        if (!shouldDeadLetter && _outboxOptions.InitialRetryDelay > TimeSpan.Zero)
        {
            // Calculate exponential backoff: delay * (backoffFactor ^ attemptCount)
            var factorPower = Math.Pow(_outboxOptions.BackoffFactor, attemptCount);
            calculatedDelay =
                TimeSpan.FromMilliseconds(_outboxOptions.InitialRetryDelay.TotalMilliseconds * factorPower);
            nextAttemptAt = DateTimeOffset.UtcNow + calculatedDelay.Value;

            _logger.LogWarning(
                "Event {EventType} dispatch failed (attempt {AttemptNumber}/{MaxAttempts}), scheduling retry with exponential backoff. " +
                "Backoff calculation: {InitialDelay}ms * ({BackoffFactor} ^ {AttemptCount}) = {CalculatedDelay}ms. " +
                "Next retry at: {NextRetryTime}. Reason: {FailureReason}",
                eventType,
                nextAttemptNumber,
                _outboxOptions.MaxRetryAttempts,
                _outboxOptions.InitialRetryDelay.TotalMilliseconds,
                _outboxOptions.BackoffFactor,
                attemptCount,
                calculatedDelay.Value.TotalMilliseconds,
                nextAttemptAt.Value,
                reason);
        }
        else if (shouldDeadLetter)
        {
            _logger.LogError(
                "Event {EventType} exceeded maximum retry attempts ({MaxAttempts}), moving to dead-letter queue. " +
                "Total attempts: {TotalAttempts}. Final failure reason: {FailureReason}",
                eventType,
                _outboxOptions.MaxRetryAttempts,
                nextAttemptNumber,
                reason);
        }
        else
        {
            _logger.LogWarning(
                "Event {EventType} dispatch failed (attempt {AttemptNumber}/{MaxAttempts}), no retry delay configured. Reason: {FailureReason}",
                eventType,
                nextAttemptNumber,
                _outboxOptions.MaxRetryAttempts,
                reason);
        }

        await _outboxStorage.MarkAsFailedAsync(@event, reason, shouldDeadLetter, nextAttemptAt, cancellationToken);

        if (shouldDeadLetter)
        {
            _logger.LogError(
                "Event {EventType} successfully moved to dead-letter queue after {TotalAttempts} failed attempts",
                eventType,
                nextAttemptNumber);
            _metrics.RecordOutboxDeadLettered(eventType);
        }
        else
        {
            _logger.LogInformation(
                "Event {EventType} marked as failed in outbox, retry scheduled for {NextRetryTime}",
                eventType,
                nextAttemptAt);
        }
    }
}