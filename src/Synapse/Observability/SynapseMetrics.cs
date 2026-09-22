using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Observability;

/// <summary>
///     Provides metrics for monitoring synapse transport operations using OpenTelemetry.
/// </summary>
public sealed class SynapseMetrics : ISynapseMetrics
{
    private readonly Counter<long> _dispatchFailures;
    private readonly Histogram<double> _dispatchLatency;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly Counter<long> _outboxMetricReadFailures;
    private int _lastKnownRetryingCount;
    private int _lastKnownDeadLetterCount;
    private double _lastKnownProcessingLagSeconds;
    private int _lastKnownQueueDepth;

    // Event dispatch metrics
    private readonly Counter<long> _eventsDispatched;
    private readonly Counter<long> _outboxDeadLettered;

    // Outbox metrics
    private readonly Counter<long> _outboxEventsProcessed;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SynapseMetrics" /> class.
    /// </summary>
    /// <param name="meterFactory">The meter factory for creating meters.</param>
    /// <param name="scopeFactory">
    ///     Optional scope factory used to resolve <see cref="IEventOutboxStorage" /> for the outbox observable
    ///     gauges. A short-lived scope is opened per gauge observation rather than the storage being captured at
    ///     construction: this type is registered Singleton, and <see cref="IEventOutboxStorage" /> can be
    ///     registered Scoped (e.g. backed by a <c>DbContext</c>), so capturing it directly would be a captive
    ///     dependency — resolvable at best, and a hard failure under <c>ValidateScopes</c>.
    /// </param>
    public SynapseMetrics(
        IMeterFactory meterFactory,
        IServiceScopeFactory? scopeFactory = null)
    {
        _scopeFactory = scopeFactory;
        var meter = meterFactory.Create("Unambitious.Synapse", "1.0.0");

        // Event dispatch metrics
        _eventsDispatched = meter.CreateCounter<long>(
            "mediator.events.dispatched",
            "{event}",
            "Number of events dispatched by distribution mode");

        _dispatchFailures = meter.CreateCounter<long>(
            "mediator.events.dispatch_failures",
            "{event}",
            "Number of event dispatch failures");

        _dispatchLatency = meter.CreateHistogram<double>(
            "mediator.events.dispatch.duration",
            "ms",
            "Duration of event dispatch operations in milliseconds");

        // Outbox metrics
        _outboxEventsProcessed = meter.CreateCounter<long>(
            "mediator.outbox.events.processed",
            "{event}",
            "Number of events processed from the outbox");

        _outboxDeadLettered = meter.CreateCounter<long>(
            "mediator.outbox.events.dead_lettered",
            "{event}",
            "Number of events moved to dead-letter queue");

        _outboxMetricReadFailures = meter.CreateCounter<long>(
            "mediator.outbox.metrics.read_failures",
            "{count}",
            "Number of failures while reading outbox observable metrics");

        // Outbox observable gauges
        meter.CreateObservableGauge(
            "mediator.outbox.queue_depth",
            ObserveOutboxQueueDepth,
            "{event}",
            "Number of pending events in the outbox");

        meter.CreateObservableGauge(
            "mediator.outbox.processing_lag",
            ObserveOutboxProcessingLag,
            "s",
            "Age of the oldest pending event in the outbox in seconds");

        meter.CreateObservableGauge(
            "mediator.outbox.retrying_count",
            ObserveOutboxRetryingCount,
            "{event}",
            "Number of events that failed at least once and are awaiting retry in the outbox");

        meter.CreateObservableGauge(
            "mediator.outbox.dead_letter_count",
            ObserveOutboxDeadLetterCount,
            "{event}",
            "Number of events that exhausted retries and were moved to the dead-letter queue");
    }

    /// <summary>
    ///     Records an event dispatch operation.
    /// </summary>
    /// <param name="eventType">The type of event dispatched.</param>
    /// <param name="success">Whether the dispatch was successful.</param>
    public void RecordEventDispatched(string eventType, bool success)
    {
        _eventsDispatched.Add(1,
            new KeyValuePair<string, object?>("event.type", eventType),
            new KeyValuePair<string, object?>("success", success));

        if (!success)
        {
            _dispatchFailures.Add(1,
                new KeyValuePair<string, object?>("event.type", eventType));
        }
    }

    /// <summary>
    ///     Records the latency of an event dispatch operation.
    /// </summary>
    /// <param name="durationMs">The duration in milliseconds.</param>
    /// <param name="eventType">The type of event dispatched.</param>
    public void RecordDispatchLatency(double durationMs, string eventType)
    {
        _dispatchLatency.Record(durationMs,
            new KeyValuePair<string, object?>("event.type", eventType));
    }

    /// <summary>
    ///     Records an event processed from the outbox.
    /// </summary>
    /// <param name="eventType">The type of event processed.</param>
    /// <param name="success">Whether the processing was successful.</param>
    public void RecordOutboxEventProcessed(string eventType, bool success)
    {
        _outboxEventsProcessed.Add(1,
            new KeyValuePair<string, object?>("event.type", eventType),
            new KeyValuePair<string, object?>("success", success));
    }

    /// <summary>
    ///     Records an event moved to the dead-letter queue.
    /// </summary>
    /// <param name="eventType">The type of event dead-lettered.</param>
    public void RecordOutboxDeadLettered(string eventType)
    {
        _outboxDeadLettered.Add(1,
            new KeyValuePair<string, object?>("event.type", eventType));
    }

    /// <summary>
    ///     Opens a short-lived scope and resolves <see cref="IEventOutboxStorage" /> from it, or <c>null</c> when
    ///     no scope factory was supplied or no storage is registered. Called once per gauge observation so this
    ///     Singleton never holds a Scoped storage past the observation that needed it.
    /// </summary>
    private (IEventOutboxStorage? Storage, IServiceScope? Scope) ResolveStorage()
    {
        if (_scopeFactory == null)
        {
            return (null, null);
        }

        var scope = _scopeFactory.CreateScope();
        return (scope.ServiceProvider.GetService<IEventOutboxStorage>(), scope);
    }

    /// <summary>
    ///     Blocks on a storage read issued from an observable-gauge callback and returns its result.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>Meter.CreateObservableGauge</c> takes a plain synchronous <see cref="Func{TResult}" /> —
    ///         <c>System.Diagnostics.Metrics</c> has no asynchronous observation callback — so the read has
    ///         to be completed here, inside the scope that owns the storage, before that scope is disposed.
    ///     </para>
    ///     <para>
    ///         Sampling the <see cref="ValueTask{T}" /> only when it happened to complete synchronously would
    ///         be wrong for any storage that genuinely goes async (a database-backed one, for instance): the
    ///         gauge would report its last-known value forever, and disposing the scope would tear the
    ///         underlying connection or <c>DbContext</c> down underneath an in-flight query, producing an
    ///         abandoned, unobserved <see cref="ObjectDisposedException" />. Blocking is safe here because
    ///         these callbacks run on a meter-listener thread, not under a legacy
    ///         <see cref="SynchronizationContext" /> that could deadlock.
    ///     </para>
    /// </remarks>
    private static T Observe<T>(ValueTask<T> read)
    {
        return read.IsCompletedSuccessfully
            ? read.Result
            : read.AsTask().GetAwaiter().GetResult();
    }

    private int ObserveOutboxQueueDepth()
    {
        var (storage, scope) = ResolveStorage();
        if (storage == null)
        {
            scope?.Dispose();
            return _lastKnownQueueDepth;
        }

        try
        {
            _lastKnownQueueDepth = Observe(storage.GetPendingCountAsync(CancellationToken.None));
            return _lastKnownQueueDepth;
        }
        catch
        {
            _outboxMetricReadFailures.Add(1,
                new KeyValuePair<string, object?>("metric.name", "queue_depth"));
            return _lastKnownQueueDepth;
        }
        finally
        {
            scope?.Dispose();
        }
    }

    private double ObserveOutboxProcessingLag()
    {
        var (storage, scope) = ResolveStorage();
        if (storage == null)
        {
            scope?.Dispose();
            return _lastKnownProcessingLagSeconds;
        }

        try
        {
            var lag = Observe(storage.GetOldestPendingAgeAsync(CancellationToken.None));
            _lastKnownProcessingLagSeconds = lag?.TotalSeconds ?? 0;
            return _lastKnownProcessingLagSeconds;
        }
        catch
        {
            _outboxMetricReadFailures.Add(1,
                new KeyValuePair<string, object?>("metric.name", "processing_lag"));
            return _lastKnownProcessingLagSeconds;
        }
        finally
        {
            scope?.Dispose();
        }
    }

    private int ObserveOutboxRetryingCount()
    {
        var (storage, scope) = ResolveStorage();
        if (storage == null)
        {
            scope?.Dispose();
            return _lastKnownRetryingCount;
        }

        try
        {
            _lastKnownRetryingCount = Observe(storage.GetRetryingCountAsync(CancellationToken.None));
            return _lastKnownRetryingCount;
        }
        catch
        {
            _outboxMetricReadFailures.Add(1,
                new KeyValuePair<string, object?>("metric.name", "retrying_count"));
            return _lastKnownRetryingCount;
        }
        finally
        {
            scope?.Dispose();
        }
    }

    private int ObserveOutboxDeadLetterCount()
    {
        var (storage, scope) = ResolveStorage();
        if (storage == null)
        {
            scope?.Dispose();
            return _lastKnownDeadLetterCount;
        }

        try
        {
            _lastKnownDeadLetterCount = Observe(storage.GetDeadLetterCountAsync(CancellationToken.None));
            return _lastKnownDeadLetterCount;
        }
        catch
        {
            _outboxMetricReadFailures.Add(1,
                new KeyValuePair<string, object?>("metric.name", "dead_letter_count"));
            return _lastKnownDeadLetterCount;
        }
        finally
        {
            scope?.Dispose();
        }
    }
}