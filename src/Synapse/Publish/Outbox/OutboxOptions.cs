namespace UnambitiousFx.Synapse.Publish.Outbox;

/// <summary>
///     Provides configuration options for the Synapse outbox implementation including retry logic,
///     exponential backoff and batch processing.
/// </summary>
public sealed record OutboxOptions
{
    /// <summary>
    ///     Maximum number of retry attempts before an event is moved to the dead-letter queue.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    ///     Initial delay applied before the first retry. When zero, retries are attempted immediately on subsequent commits.
    /// </summary>
    public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.Zero;

    /// <summary>
    ///     Backoff multiplier applied to the delay for each additional attempt (attempt^factor).
    /// </summary>
    public double BackoffFactor { get; set; } = 2d;

    /// <summary>
    ///     The maximum number of events processed per commit invocation.
    /// </summary>
    public int? BatchSize { get; set; }

    /// <summary>
    ///     How long a processor holds the entries it claimed from a storage that implements
    ///     <see cref="Abstractions.IClaimableOutboxStorage" />. Marking an entry processed or failed releases it
    ///     sooner; an entry whose processor crashed becomes claimable again once this elapses.
    /// </summary>
    /// <remarks>
    ///     Keep it comfortably longer than dispatching one batch takes: if a lease expires mid-dispatch, another
    ///     processor can claim and dispatch the same entry.
    /// </remarks>
    public TimeSpan ClaimLeaseDuration { get; set; } = TimeSpan.FromMinutes(5);
}