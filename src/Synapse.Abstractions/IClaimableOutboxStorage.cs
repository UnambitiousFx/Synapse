namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Optional capability of an <see cref="IEventOutboxStorage" /> that can hand pending entries to exactly one
///     processor at a time, so several instances of an application can drain the same storage without
///     dispatching an entry twice.
/// </summary>
/// <remarks>
///     <para>
///         A claim is a lease, not a held lock: a claimed entry is invisible to other claimers (and to
///         <see cref="IEventOutboxStorage.GetPendingEventsAsync" />) until its lease expires or it is marked with
///         <see cref="IEventOutboxStorage.MarkAsProcessedAsync" /> or
///         <see cref="IEventOutboxStorage.MarkAsFailedAsync" />, which release it. A processor that crashes while
///         holding a claim therefore delays its entries by at most the lease duration; after that they are
///         claimable again.
///     </para>
///     <para>
///         Delivery stays at-least-once. If processing a claimed entry takes longer than the lease, another processor
///         can claim and dispatch it too, so choose a lease comfortably longer than a batch takes to dispatch.
///     </para>
/// </remarks>
public interface IClaimableOutboxStorage
{
    /// <summary>
    ///     Atomically claims pending entries that are due for dispatch and not currently claimed, oldest first.
    /// </summary>
    /// <param name="maxCount">The maximum number of entries to claim, or <see langword="null" /> for no limit.</param>
    /// <param name="leaseDuration">How long the claim holds before the entries become claimable again.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>
    ///     The claimed entries. An entry is returned to at most one concurrent caller, including callers in other
    ///     processes when the storage is shared.
    /// </returns>
    ValueTask<IReadOnlyList<OutboxEntry>> ClaimPendingEventsAsync(int? maxCount,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);
}
