namespace UnambitiousFx.Synapse.Observability;

/// <summary>
///     Defines metrics instrumentation for key events and operations within
///     the mediator system, including message publishing, consumption, retries, latency tracking,
///     and outbox processing activities.
/// </summary>
public interface ISynapseMetrics
{
    /// <summary>
    ///     Records an event dispatch operation.
    /// </summary>
    /// <param name="eventType">The type of event dispatched.</param>
    /// <param name="success">Whether the dispatch was successful.</param>
    void RecordEventDispatched(string eventType, bool success);

    /// <summary>
    ///     Records the latency of an event dispatch operation.
    /// </summary>
    /// <param name="durationMs">The duration in milliseconds.</param>
    /// <param name="eventType">The type of event dispatched.</param>
    void RecordDispatchLatency(double durationMs, string eventType);

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