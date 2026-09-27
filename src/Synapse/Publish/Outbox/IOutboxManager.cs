using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Publish.Outbox;

internal interface IOutboxManager
{
    /// <summary>
    ///     Processes pending events from the outbox.
    ///     Retrieves pending events and dispatches them using registered dispatcher delegates.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A combined result of all processed events.</returns>
    ValueTask<Result> ProcessPendingAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     Processes one batch of pending events, claiming them first when the storage supports it.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many events the batch held, and the combined result of dispatching them.</returns>
    ValueTask<OutboxBatchResult> ProcessBatchAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     Stores the specified event in the outbox for later processing.
    /// </summary>
    /// <param name="event">The event to be stored.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <typeparam name="TEvent">
    ///     The type of the event. Must implement <see cref="IEvent" /> and have a parameterless constructor.
    /// </typeparam>
    /// <returns>A result indicating the success or failure of the operation.</returns>
    ValueTask<Result> StoreAsync<TEvent>(TEvent @event,
        CancellationToken cancellationToken)
        where TEvent : class, IEvent;

    /// <summary>
    ///     Discards the events this scope stored and that have not been dispatched yet, so a request that failed
    ///     does not leave events behind for an unrelated later commit to dispatch.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    ///     A successful result when the events were discarded or the storage has nothing to discard, otherwise the
    ///     storage's failure.
    /// </returns>
    ValueTask<Result> DiscardStoredAsync(CancellationToken cancellationToken);
}

/// <summary>
///     The outcome of processing one outbox batch.
/// </summary>
/// <param name="Count">The number of events the batch held.</param>
/// <param name="Result">The combined result of dispatching them.</param>
internal readonly record struct OutboxBatchResult(int Count, Result Result);
