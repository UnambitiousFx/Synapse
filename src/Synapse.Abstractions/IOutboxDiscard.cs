using UnambitiousFx.Functional;

namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Takes back the outbox events the current scope stored and that have not been dispatched yet.
/// </summary>
/// <remarks>
///     <para>
///         The in-memory outbox storage is not enlisted in any database transaction, so a request that stored an
///         event and then failed would otherwise leave that event behind for the next unrelated
///         <see cref="IOutboxCommit.CommitAsync" /> to dispatch. Discarding is what stands in for the rollback.
///     </para>
///     <para>
///         It is scope-wide: it takes back everything the scope stored, not only what one request stored. A storage
///         that cannot discard, such as a transactional one, is left untouched and the call succeeds.
///     </para>
/// </remarks>
public interface IOutboxDiscard
{
    /// <summary>
    ///     Discards the events stored by the current scope that are still pending.
    /// </summary>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A result indicating the success or failure of the operation.</returns>
    ValueTask<Result> DiscardStoredAsync(CancellationToken cancellationToken = default);
}
