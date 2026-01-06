namespace UnambitiousFx.Synapse.Observability;

/// <summary>
///     Defines metrics instrumentation for key events and operations within
///     the mediator system, including message publishing, consumption, retries, latency tracking,
///     and outbox processing activities.
/// </summary>
public interface ISynapseMetrics
{
    /// <summary>
    ///     Records a message published to a transport.
    /// </summary>
    /// <param name="messageType">The type of message published.</param>
    /// <param name="transportName">The name of the transport.</param>
    void RecordPublished(string messageType, string transportName);

    /// <summary>
    ///     Records a message consumed from a transport.
    /// </summary>
    /// <param name="messageType">The type of message consumed.</param>
    /// <param name="transportName">The name of the transport.</param>
    void RecordConsumed(string messageType, string transportName);

    /// <summary>
    ///     Records a message retry attempt.
    /// </summary>
    /// <param name="messageType">The type of message being retried.</param>
    void RecordRetry(string messageType);

    /// <summary>
    ///     Records a message sent to dead-letter queue.
    /// </summary>
    /// <param name="messageType">The type of message dead-lettered.</param>
    void RecordDeadLettered(string messageType);

    /// <summary>
    ///     Records the latency of a publish operation.
    /// </summary>
    /// <param name="durationMs">The duration in milliseconds.</param>
    /// <param name="messageType">The type of message published.</param>
    /// <param name="transportName">The name of the transport.</param>
    void RecordPublishLatency(double durationMs, string messageType, string transportName);

    /// <summary>
    ///     Records the latency of a consume operation.
    /// </summary>
    /// <param name="durationMs">The duration in milliseconds.</param>
    /// <param name="messageType">The type of message consumed.</param>
    /// <param name="transportName">The name of the transport.</param>
    void RecordConsumeLatency(double durationMs, string messageType, string transportName);

    /// <summary>
    ///     Records an event dispatch operation.
    /// </summary>
    /// <param name="eventType">The type of event dispatched.</param>
    /// <param name="distributionMode">The distribution mode used for dispatch.</param>
    /// <param name="success">Whether the dispatch was successful.</param>
    void RecordEventDispatched(string eventType, string distributionMode, bool success);

    /// <summary>
    ///     Records the latency of an event dispatch operation.
    /// </summary>
    /// <param name="durationMs">The duration in milliseconds.</param>
    /// <param name="eventType">The type of event dispatched.</param>
    /// <param name="distributionMode">The distribution mode used for dispatch.</param>
    void RecordDispatchLatency(double durationMs, string eventType, string distributionMode);

    /// <summary>
    ///     Records an event processed from the outbox.
    /// </summary>
    /// <param name="eventType">The type of event processed.</param>
    /// <param name="success">Whether the processing was successful.</param>
    void RecordOutboxEventProcessed(string eventType, bool success);

    /// <summary>
    ///     Records an event moved to the dead-letter queue.
    /// </summary>
    /// <param name="eventType">The type of event dead-lettered.</param>
    void RecordOutboxDeadLettered(string eventType);
}