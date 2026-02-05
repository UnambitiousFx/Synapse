using UnambitiousFx.Functional;

namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Provides functionality to dispatch events in an asynchronous manner.
/// </summary>
public interface IEventDispatcher
{
    /// <summary>
    ///     Dispatches an event asynchronously within the specified context.
    /// </summary>
    /// <typeparam name="TEvent">The type of the event to dispatch, which must implement <see cref="IEvent" />.</typeparam>
    /// <param name="event">The event to be dispatched.</param>
    /// <param name="distributionMode">Specifies the distribution mode for the event.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to request cancellation of the operation.</param>
    /// <returns>
    ///     A task that represents the asynchronous operation. The task's result contains a <see cref="Result" />
    ///     indicating the outcome of the operation.
    /// </returns>
    ValueTask<Result> DispatchAsync<TEvent>(TEvent @event,
        DistributionMode distributionMode,
        CancellationToken cancellationToken = default)
        where TEvent : class, IEvent;
}