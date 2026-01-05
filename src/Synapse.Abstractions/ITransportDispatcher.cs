namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Represents a transport dispatcher responsible for sending messages to their respective destinations.
/// </summary>
public interface ITransportDispatcher
{
    /// <summary>
    ///     Dispatches the specified event asynchronously to the appropriate transport layer.
    /// </summary>
    /// <typeparam name="T">
    ///     The type of the event to be dispatched. It must be a reference type and have a parameterless constructor.
    /// </typeparam>
    /// <param name="event">
    ///     The event instance to dispatch.
    /// </param>
    /// <param name="cancellationToken">
    ///     A token to monitor for cancellation requests.
    /// </param>
    /// <returns>
    ///     A task that represents the asynchronous operation of dispatching the event.
    /// </returns>
    ValueTask DispatchAsync<T>(
        T @event,
        CancellationToken cancellationToken = default)
        where T : class, IEvent;
}