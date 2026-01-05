using System.Diagnostics.Metrics;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Observability;

/// <summary>
///     Provides metrics for monitoring mediator transport operations using OpenTelemetry.
/// </summary>
public sealed class MediatorMetrics : IMediatorMetrics
{
    private readonly Histogram<double> _consumeLatency;
    private readonly Counter<long> _dispatchFailures;
    private readonly Histogram<double> _dispatchLatency;
    private readonly IEventOutboxStorage? _eventOutboxStorage;

    // Event dispatch metrics
    private readonly Counter<long> _eventsDispatched;
    private readonly Counter<long> _messagesConsumed;
    private readonly Counter<long> _messagesDeadLettered;
    private readonly Counter<long> _messagesPublished;
    private readonly Counter<long> _messagesRetried;
    private readonly Counter<long> _outboxDeadLettered;

    // Outbox metrics
    private readonly Counter<long> _outboxEventsProcessed;
    private readonly Histogram<double> _publishLatency;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MediatorMetrics" /> class.
    /// </summary>
    /// <param name="meterFactory">The meter factory for creating meters.</param>
    /// <param name="eventOutboxStorage">Optional event outbox storage for queue depth metrics.</param>
    public MediatorMetrics(
        IMeterFactory meterFactory,
        IEventOutboxStorage? eventOutboxStorage = null)
    {
        _eventOutboxStorage = eventOutboxStorage;
        var meter = meterFactory.Create("Unambitious.Synapse", "1.0.0");

        // Counters
        _messagesPublished = meter.CreateCounter<long>(
            "mediator.messages.published",
            "{message}",
            "Number of messages published to transports");

        _messagesConsumed = meter.CreateCounter<long>(
            "mediator.messages.consumed",
            "{message}",
            "Number of messages consumed from transports");

        _messagesRetried = meter.CreateCounter<long>(
            "mediator.messages.retried",
            "{message}",
            "Number of message processing retries");

        _messagesDeadLettered = meter.CreateCounter<long>(
            "mediator.messages.dead_lettered",
            "{message}",
            "Number of messages sent to dead-letter queue");

        // Histograms
        _publishLatency = meter.CreateHistogram<double>(
            "mediator.publish.duration",
            "ms",
            "Duration of message publish operations in milliseconds");

        _consumeLatency = meter.CreateHistogram<double>(
            "mediator.consume.duration",
            "ms",
            "Duration of message consume operations in milliseconds");

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

        // Observable Gauge for event outbox queue depth
        if (_eventOutboxStorage != null)
            meter.CreateObservableGauge(
                "mediator.outbox.queue_depth",
                ObserveEventOutboxQueueDepth,
                "{event}",
                "Number of pending events in the event outbox");
    }

    /// <summary>
    ///     Records a message published to a transport.
    /// </summary>
    /// <param name="messageType">The type of message published.</param>
    /// <param name="transportName">The name of the transport.</param>
    public void RecordPublished(string messageType, string transportName)
    {
        _messagesPublished.Add(1, new KeyValuePair<string, object?>("message.type", messageType),
            new KeyValuePair<string, object?>("transport.name", transportName));
    }

    /// <summary>
    ///     Records a message consumed from a transport.
    /// </summary>
    /// <param name="messageType">The type of message consumed.</param>
    /// <param name="transportName">The name of the transport.</param>
    public void RecordConsumed(string messageType, string transportName)
    {
        _messagesConsumed.Add(1, new KeyValuePair<string, object?>("message.type", messageType),
            new KeyValuePair<string, object?>("transport.name", transportName));
    }

    /// <summary>
    ///     Records a message retry attempt.
    /// </summary>
    /// <param name="messageType">The type of message being retried.</param>
    public void RecordRetry(string messageType)
    {
        _messagesRetried.Add(1, new KeyValuePair<string, object?>("message.type", messageType));
    }

    /// <summary>
    ///     Records a message sent to dead-letter queue.
    /// </summary>
    /// <param name="messageType">The type of message dead-lettered.</param>
    public void RecordDeadLettered(string messageType)
    {
        _messagesDeadLettered.Add(1, new KeyValuePair<string, object?>("message.type", messageType));
    }

    /// <summary>
    ///     Records the latency of a publish operation.
    /// </summary>
    /// <param name="durationMs">The duration in milliseconds.</param>
    /// <param name="messageType">The type of message published.</param>
    /// <param name="transportName">The name of the transport.</param>
    public void RecordPublishLatency(double durationMs, string messageType, string transportName)
    {
        _publishLatency.Record(durationMs, new KeyValuePair<string, object?>("message.type", messageType),
            new KeyValuePair<string, object?>("transport.name", transportName));
    }

    /// <summary>
    ///     Records the latency of a consume operation.
    /// </summary>
    /// <param name="durationMs">The duration in milliseconds.</param>
    /// <param name="messageType">The type of message consumed.</param>
    /// <param name="transportName">The name of the transport.</param>
    public void RecordConsumeLatency(double durationMs, string messageType, string transportName)
    {
        _consumeLatency.Record(durationMs, new KeyValuePair<string, object?>("message.type", messageType),
            new KeyValuePair<string, object?>("transport.name", transportName));
    }

    /// <summary>
    ///     Records an event dispatch operation.
    /// </summary>
    /// <param name="eventType">The type of event dispatched.</param>
    /// <param name="distributionMode">The distribution mode used for dispatch.</param>
    /// <param name="success">Whether the dispatch was successful.</param>
    public void RecordEventDispatched(string eventType, string distributionMode, bool success)
    {
        _eventsDispatched.Add(1,
            new KeyValuePair<string, object?>("event.type", eventType),
            new KeyValuePair<string, object?>("distribution.mode", distributionMode),
            new KeyValuePair<string, object?>("success", success));

        if (!success)
            _dispatchFailures.Add(1,
                new KeyValuePair<string, object?>("event.type", eventType),
                new KeyValuePair<string, object?>("distribution.mode", distributionMode));
    }

    /// <summary>
    ///     Records the latency of an event dispatch operation.
    /// </summary>
    /// <param name="durationMs">The duration in milliseconds.</param>
    /// <param name="eventType">The type of event dispatched.</param>
    /// <param name="distributionMode">The distribution mode used for dispatch.</param>
    public void RecordDispatchLatency(double durationMs, string eventType, string distributionMode)
    {
        _dispatchLatency.Record(durationMs,
            new KeyValuePair<string, object?>("event.type", eventType),
            new KeyValuePair<string, object?>("distribution.mode", distributionMode));
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

    private long ObserveEventOutboxQueueDepth()
    {
        if (_eventOutboxStorage == null) return 0;

        try
        {
            // Get pending events count
            // Note: This is a synchronous call in an observable callback
            // GetPendingEventsAsync should be fast for counting purposes
            var pendingEvents = _eventOutboxStorage.GetPendingEventsAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            return pendingEvents.Count();
        }
        catch
        {
            // Return 0 if unable to get queue depth
            return 0;
        }
    }
}