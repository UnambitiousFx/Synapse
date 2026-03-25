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

    // Event dispatch metrics
    private readonly Counter<long> _eventsDispatched;
    private readonly Counter<long> _outboxDeadLettered;

    // Outbox metrics
    private readonly Counter<long> _outboxEventsProcessed;
    private readonly ObservableGauge<int> _outboxQueueDepth;
    private readonly ObservableGauge<double> _outboxProcessingLag;
    private readonly ObservableGauge<int> _outboxFailedCount;

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

        // Observable Gauges for event outbox metrics
        _outboxQueueDepth = meter.CreateObservableGauge(
            "mediator.outbox.queue_depth",
            ObserveOutboxQueueDepth,
            "{event}",
            "Number of pending events in the outbox");

        _outboxProcessingLag = meter.CreateObservableGauge(
            "mediator.outbox.processing_lag",
            ObserveOutboxProcessingLag,
            "s",
            "Age of the oldest pending event in seconds");

        _outboxFailedCount = meter.CreateObservableGauge(
            "mediator.outbox.failed_count",
            ObserveOutboxFailedCount,
            "{event}",
            "Number of failed events awaiting retry");
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
            _dispatchFailures.Add(1,
                new KeyValuePair<string, object?>("event.type", eventType));
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
    ///     Records the current queue depth of the outbox.
    /// </summary>
    /// <param name="count">The number of pending events.</param>
    public void RecordOutboxQueueDepth(int count)
    {
        // This is recorded via observable gauge, no manual recording needed
        // Method kept for interface compatibility
    }

    /// <summary>
    ///     Records the processing lag of the outbox.
    /// </summary>
    /// <param name="lagSeconds">The age of the oldest pending event in seconds.</param>
    public void RecordOutboxProcessingLag(double lagSeconds)
    {
        // This is recorded via observable gauge, no manual recording needed
        // Method kept for interface compatibility
    }

    /// <summary>
    ///     Records the number of failed events in the outbox.
    /// </summary>
    /// <param name="count">The number of failed events.</param>
    public void RecordOutboxFailedCount(int count)
    {
        // This is recorded via observable gauge, no manual recording needed
        // Method kept for interface compatibility
    }

    private int ObserveOutboxQueueDepth()
    {
        if (_eventOutboxStorage == null) return 0;

        try
        {
            return _eventOutboxStorage.GetPendingCountAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }
        catch
        {
            return 0;
        }
    }

    private double ObserveOutboxProcessingLag()
    {
        if (_eventOutboxStorage == null) return 0;

        try
        {
            var lag = _eventOutboxStorage.GetOldestPendingAgeAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            return lag?.TotalSeconds ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    private int ObserveOutboxFailedCount()
    {
        if (_eventOutboxStorage == null) return 0;

        try
        {
            return _eventOutboxStorage.GetFailedCountAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }
        catch
        {
            return 0;
        }
    }
}