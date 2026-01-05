namespace UnambitiousFx.Synapse.Publish.Outbox;

/// <summary>
///     Represents the processing status of an outbox entry.
/// </summary>
public enum OutboxEntryStatus
{
    /// <summary>
    ///     The entry is pending and has not been processed yet.
    /// </summary>
    Pending = 0,

    /// <summary>
    ///     The entry is currently being processed.
    /// </summary>
    Processing = 1,

    /// <summary>
    ///     The entry has been successfully processed and dispatched.
    /// </summary>
    Completed = 2,

    /// <summary>
    ///     The entry has failed processing after all retry attempts.
    /// </summary>
    Failed = 3
}