using UnambitiousFx.Functional;

namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Represents a service responsible for committing pending events from the outbox.
/// </summary>
/// <remarks>
///     <para>
///         This interface provides explicit separation of concerns between event publishing
///         and outbox transaction management, following the Unit of Work pattern.
///     </para>
///     <para>
///         When using the outbox pattern with <see cref="EmitMode.Outbox" />, events
///         are stored but not immediately dispatched. The <see cref="CommitAsync" /> method
///         processes all pending events in the current scope and dispatches them.
///     </para>
/// </remarks>
public interface IOutboxCommit
{
    /// <summary>
    ///     Commits and processes all pending events from the outbox in the current scope.
    /// </summary>
    /// <param name="cancellationToken">
    ///     An optional cancellation token to cancel the operation before completion if needed.
    /// </param>
    /// <returns>
    ///     A <see cref="ValueTask{TResult}" /> containing the result of the commit operation.
    ///     Returns success if all pending events were successfully processed,
    ///     or failure if any event processing failed.
    /// </returns>
    /// <remarks>
    ///     This method retrieves the pending events from the <see cref="IEventOutboxStorage" /> and dispatches them
    ///     according to their configured distribution mode. Retrieval is not filtered by the scope that stored an
    ///     entry: what ties an entry back to the flow that produced it is
    ///     <see cref="OutboxEntry.Headers" />, captured at store time and travelling with the entry, which is what
    ///     lets processing run from a scope — or a process — that never stored anything.
    /// </remarks>
    ValueTask<Result> CommitAsync(CancellationToken cancellationToken = default);
}