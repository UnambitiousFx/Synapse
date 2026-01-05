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
    ///     Stores the specified event in the outbox for later processing.
    /// </summary>
    /// <param name="event">The event to be stored.</param>
    /// <param name="distributionMode">Specifies the distribution mode for the event (e.g., local, external, or both).</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <typeparam name="TEvent">
    ///     The type of the event. Must implement <see cref="IEvent" /> and have a parameterless constructor.
    /// </typeparam>
    /// <returns>A result indicating the success or failure of the operation.</returns>
    ValueTask<Result> StoreAsync<TEvent>(TEvent @event, DistributionMode distributionMode,
        CancellationToken cancellationToken)
        where TEvent : class, IEvent;
}