using UnambitiousFx.Functional;

namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Optional capability of an <see cref="IEventOutboxStorage" /> that can take back entries which were stored but
///     not yet dispatched.
/// </summary>
/// <remarks>
///     Storages that write in the same database transaction as the business change do not need this: the
///     transaction rolling back already removes the entry. It exists for storages that are not enlisted in any
///     transaction, such as the in-memory one, where a failed request would otherwise leave its events behind for an
///     unrelated later commit to dispatch.
/// </remarks>
public interface IDiscardableOutboxStorage
{
    /// <summary>
    ///     Removes the entries that were stored for the given event instances and have not been dispatched yet.
    /// </summary>
    /// <param name="events">
    ///     The event instances to discard. Matched by reference, since two emissions of value-equal events are still
    ///     two distinct entries.
    /// </param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A result indicating the success or failure of the operation.</returns>
    ValueTask<Result> DiscardAsync(IReadOnlyCollection<IEvent> events,
        CancellationToken cancellationToken = default);
}
