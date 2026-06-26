using System.Diagnostics.Metrics;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Observability;

/// <summary>
///     Provides metrics for monitoring synapse transport operations using OpenTelemetry.
/// </summary>
public sealed class SynapseMetrics : ISynapseMetrics
{
    private readonly Counter<long> _dispatchFailures;
    private readonly Histogram<double> _dispatchLatency;
    private readonly IEventOutboxStorage? _eventOutboxStorage;
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
    /// <param name="eventOutboxStorage">Optional event outbox storage for queue depth metrics.</param>
    public SynapseMetrics(
        IMeterFactory meterFactory,
        IEventOutboxStorage? eventOutboxStorage = null)
    {
        _eventOutboxStorage = eventOutboxStorage;
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

    private int ObserveOutboxQueueDepth()
    {
        if (_eventOutboxStorage == null)
        {
            return 0;
        }

        try
        {
            var pendingCount = _eventOutboxStorage.GetPendingCountAsync(CancellationToken.None);
            if (pendingCount.IsCompletedSuccessfully)
            {
                _lastKnownQueueDepth = pendingCount.Result;
            }

            return _lastKnownQueueDepth;
        }
        catch
        {
            _outboxMetricReadFailures.Add(1,
                new KeyValuePair<string, object?>("metric.name", "queue_depth"));
            return _lastKnownQueueDepth;
        }
    }

    private double ObserveOutboxProcessingLag()
    {
        if (_eventOutboxStorage == null)
        {
            return 0;
        }

        try
        {
            var lag = _eventOutboxStorage.GetOldestPendingAgeAsync(CancellationToken.None);
            if (lag.IsCompletedSuccessfully)
            {
                _lastKnownProcessingLagSeconds = lag.Result?.TotalSeconds ?? 0;
            }

            return _lastKnownProcessingLagSeconds;
        }
        catch
        {
            _outboxMetricReadFailures.Add(1,
                new KeyValuePair<string, object?>("metric.name", "processing_lag"));
            return _lastKnownProcessingLagSeconds;
        }
    }

    private int ObserveOutboxRetryingCount()
    {
        if (_eventOutboxStorage == null)
        {
            return 0;
        }

        try
        {
            var retryingCount = _eventOutboxStorage.GetRetryingCountAsync(CancellationToken.None);
            if (retryingCount.IsCompletedSuccessfully)
            {
                _lastKnownRetryingCount = retryingCount.Result;
            }

            return _lastKnownRetryingCount;
        }
        catch
        {
            _outboxMetricReadFailures.Add(1,
                new KeyValuePair<string, object?>("metric.name", "retrying_count"));
            return _lastKnownRetryingCount;
        }
    }

    private int ObserveOutboxDeadLetterCount()
    {
        if (_eventOutboxStorage == null)
        {
            return 0;
        }

        try
        {
            var deadLetterCount = _eventOutboxStorage.GetDeadLetterCountAsync(CancellationToken.None);
            if (deadLetterCount.IsCompletedSuccessfully)
            {
                _lastKnownDeadLetterCount = deadLetterCount.Result;
            }

            return _lastKnownDeadLetterCount;
        }
        catch
        {
            _outboxMetricReadFailures.Add(1,
                new KeyValuePair<string, object?>("metric.name", "dead_letter_count"));
            return _lastKnownDeadLetterCount;
        }
    }
}