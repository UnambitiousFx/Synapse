using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Observability;
using UnambitiousFx.Synapse.Propagation;

namespace UnambitiousFx.Synapse.Publish.Outbox;

/// <summary>
///     Manages outbox storage and dispatch strategies
///     Coordinates event storage, retry logic, and distribution mode-based dispatch.
/// </summary>
internal sealed class OutboxManager : IOutboxManager
{
    private static readonly IReadOnlyDictionary<string, string> EmptyHeaders =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private readonly IContextAccessor _contextAccessor;
    private readonly ILogger<OutboxManager> _logger;
    private readonly ISynapseMetrics _metrics;
    private readonly EventDispatcherOptions _options;
    private readonly OutboxOptions _outboxOptions;
    private readonly IEventOutboxStorage _outboxStorage;
    private readonly IContextPropagator _propagator;
    private readonly IServiceScopeFactory _scopeFactory;

    public OutboxManager(
        IEventOutboxStorage outboxStorage,
        IServiceScopeFactory scopeFactory,
        ISynapseMetrics metrics,
        IContextPropagator propagator,
        IContextAccessor contextAccessor,
        IOptions<EventDispatcherOptions> options,
        IOptions<OutboxOptions> outboxOptions,
        ILogger<OutboxManager> logger)
    {
        _outboxStorage = outboxStorage;
        _scopeFactory = scopeFactory;
        _propagator = propagator;
        _contextAccessor = contextAccessor;
        _options = options.Value;
        _outboxOptions = outboxOptions.Value;
        _logger = logger;
        _metrics = metrics;
    }

    public async ValueTask<Result> ProcessPendingAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Processing pending events from outbox");

        var pendingEvents = await _outboxStorage.GetPendingEventsAsync(cancellationToken);
        var entries = _outboxOptions.BatchSize.HasValue
            ? pendingEvents.Take(_outboxOptions.BatchSize.Value).ToList()
            : pendingEvents.ToList();

        if (entries.Count == 0)
        {
            _logger.LogDebug("No pending events found in outbox");
            return Result.Success();
        }

        _logger.LogInformation(
            "Processing {EventCount} pending events from outbox (batch size: {BatchSize})",
            entries.Count, _outboxOptions.BatchSize);

        var results = new List<Result>();

        foreach (var entry in entries)
        {
            var result = await DispatchEventAsync(entry, cancellationToken);
            results.Add(result);
        }

        var combinedResult = results.Combine();

        if (combinedResult.IsSuccess)
        {
            _logger.LogInformation(
                "Successfully processed {EventCount} pending events from outbox",
                entries.Count);
        }
        else
        {
            _logger.LogWarning(
                "Completed processing {EventCount} pending events from outbox with failures: {Error}",
                entries.Count, combinedResult.ToString());
        }

        return combinedResult;
    }

    public ValueTask<Result> StoreAsync<TEvent>(TEvent @event,
        CancellationToken cancellationToken)
        where TEvent : class, IEvent
    {
        return _outboxStorage.AddAsync(@event, CaptureHeaders(), cancellationToken);
    }

    /// <summary>
    ///     Snapshots the ambient flow state so a later dispatch can be tied back to the action that stored the
    ///     event.
    /// </summary>
    /// <remarks>
    ///     Existence is checked with <see cref="IContextAccessor.IsInitialized" /> rather than by reading
    ///     <see cref="IContextAccessor.Context" />, because reading it is what creates a context. Storing an event
    ///     outside any unit of work must not invent a flow for it to belong to.
    /// </remarks>
    private IReadOnlyDictionary<string, string> CaptureHeaders()
    {
        if (!_contextAccessor.IsInitialized)
        {
            return EmptyHeaders;
        }

        var carrier = new DictionaryPropagationCarrier();
        _propagator.Inject(_contextAccessor.Context, carrier);
        return carrier.Headers;
    }

    /// <summary>
    ///     Copies a stored entry's headers into the case-insensitive dictionary the carrier expects.
    /// </summary>
    /// <remarks>
    ///     Written as a loop rather than <c>ToDictionary</c> because <see cref="IEventOutboxStorage" /> is a public
    ///     extension point: an implementation that round-trips headers through a case-sensitive column can return
    ///     both <c>Trace-Id</c> and <c>trace-id</c>, and <c>ToDictionary</c> throws on the duplicate. Thrown from
    ///     where it used to be — outside the <c>try</c> — that aborted the whole batch, leaving every entry in it
    ///     neither processed nor marked failed (see known issue 041). Last value wins, matching what the carrier
    ///     itself does on a repeated <c>Set</c>.
    /// </remarks>
    private static Dictionary<string, string> ReadHeaders(OutboxEntry entry)
    {
        var headers = new Dictionary<string, string>(entry.Headers.Count, StringComparer.OrdinalIgnoreCase);

        foreach (var header in entry.Headers)
        {
            headers[header.Key] = header.Value;
        }

        return headers;
    }

    private async ValueTask<Result> DispatchEventAsync(
        OutboxEntry entry,
        CancellationToken cancellationToken)
    {
        var @event = entry.Event;
        var eventType = @event.GetType().Name;

        try
        {
            var restored = _propagator.Extract(new DictionaryPropagationCarrier(ReadHeaders(entry)));

            // The stored trace context becomes the dispatch span's parent, so the whole business flow — the request
            // that stored the entry and the work that results from it — shares one trace id and shows up as a single
            // trace. The parent span has already ended by now, which is expected for an entry dispatched later.
            //
            // A future batch consumer, handling several messages with several different parents in one span, must use
            // ActivityLink instead; parenting only expresses a single cause.
            using var activity = SynapseActivitySource.Source.StartActivity(
                "synapse.outbox.dispatch",
                ActivityKind.Consumer,
                restored.Trace);

            activity?.SetTag("synapse.mediator.event_type", eventType);

            _logger.LogDebug("Dispatching event {EventType} from outbox", eventType);

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

            // Dispatching an entry is its own unit of work, so it gets its own scope and its own context, built
            // from what the entry carries. Running it in the caller's scope instead gave the handlers the
            // caller's context: the restored trace context reached the dispatch span but never the context the
            // handlers read, the stored baggage was dropped entirely, and — because pending entries are
            // retrieved across scopes — a request committing the outbox dispatched other requests' entries under
            // its own identity.
            await using var scope = _scopeFactory.CreateAsyncScope();

            // Written before anything in the scope resolves IContext, which is what materializes it. Nothing has
            // yet, the scope being one statement old.
            scope.ServiceProvider.GetRequiredService<IInboundContextStore>()
                .Inbound = restored;

            // The dispatcher delegate calls EventDispatcher.DispatchFromOutboxAsync<TEvent>
            // with the correct generic type, avoiding reflection
            var result = await dispatcher(@event,
                scope.ServiceProvider.GetRequiredService<IEventDispatcher>(),
                cancellationToken);

            if (result.IsSuccess)
            {
                _logger.LogDebug(
                    "Event {EventType} dispatched successfully from outbox, marking as processed",
                    eventType);
                await _outboxStorage.MarkAsProcessedAsync(entry.Id, cancellationToken);
                _metrics.RecordOutboxEventProcessed(eventType, true);
            }
            else
            {
                _logger.LogWarning(
                    "Event {EventType} dispatch from outbox failed: {Error}",
                    eventType, result.ToString());
                await HandleDispatchFailureAsync(entry, result.ToString(), cancellationToken);
                _metrics.RecordOutboxEventProcessed(eventType, false);
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(
                "Outbox dispatch was canceled while processing event {EventType}",
                eventType);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Exception occurred while dispatching event {EventType} from outbox",
                eventType);
            await HandleDispatchFailureAsync(entry, ex.Message, cancellationToken);
            return Result.Failure(ex.Message);
        }
    }

    /// <summary>
    ///     Handles dispatch failures by calculating retry delays and moving events to dead-letter when appropriate.
    /// </summary>
    /// <param name="entry">The stored outbox item that failed to dispatch.</param>
    /// <param name="reason">The reason for the failure.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async ValueTask HandleDispatchFailureAsync(
        OutboxEntry entry,
        string reason,
        CancellationToken cancellationToken)
    {
        var eventType = entry.Event.GetType().Name;
        var attemptCount = await _outboxStorage.GetAttemptCountAsync(entry.Id, cancellationToken) ?? 0;
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

        await _outboxStorage.MarkAsFailedAsync(entry.Id, reason, shouldDeadLetter, nextAttemptAt, cancellationToken);

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