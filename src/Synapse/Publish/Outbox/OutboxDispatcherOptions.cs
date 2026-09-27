namespace UnambitiousFx.Synapse.Publish.Outbox;

/// <summary>
///     Configures the background outbox dispatcher enabled with
///     <see cref="ISynapseConfig.AddOutboxDispatcher" />.
/// </summary>
/// <remarks>
///     Retry, dead-letter, batch size and claim lease settings are shared with the manual
///     <see cref="Abstractions.IOutboxCommit.CommitAsync" /> path and live on <see cref="OutboxOptions" />.
/// </remarks>
public sealed record OutboxDispatcherOptions
{
    /// <summary>
    ///     How long the dispatcher waits between polls once the outbox has nothing more to dispatch. A poll that
    ///     dispatches a full batch (see <see cref="OutboxOptions.BatchSize" />) successfully is followed by another
    ///     one straight away.
    /// </summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);
}
