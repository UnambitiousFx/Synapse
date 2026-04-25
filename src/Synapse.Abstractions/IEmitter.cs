using UnambitiousFx.Functional;

namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Represents a publisher capable of handling and publishing events to their respective subscribers or processors.
/// </summary>
public interface IEmitter
{
    /// <summary>
    ///     Publishes an event asynchronously within the given context and provides feedback about success or failure.
    /// </summary>
    /// <typeparam name="TEvent">
    ///     The type of the event to be published. This must implement the <see cref="IEvent" />
    ///     interface.
    /// </typeparam>
    /// <param name="event">The event instance to be published.</param>
    /// <param name="cancellationToken">An optional cancellation token to cancel the operation.</param>
    /// <returns>A <see cref="ValueTask{TResult}" /> containing the result of the publish operation.</returns>
    ValueTask<Result> EmitAsync<TEvent>(TEvent @event,
        CancellationToken cancellationToken = default)
        where TEvent : class, IEvent;

    /// <summary>
    ///     Publishes an event asynchronously within the specified context using the given publish mode and distribution mode,
    ///     and provides feedback about success or failure.
    /// </summary>
    /// <typeparam name="TEvent">
    ///     The type of the event to be published. This must implement the <see cref="IEvent" />
    ///     interface.
    /// </typeparam>
    /// <param name="event">The event instance to be published.</param>
    /// <param name="emitMode">
    ///     Specifies the mode in which the event should be published, such as immediately (Now),
    ///     through an outbox mechanism, or using the default behavior.
    /// </param>
    /// <param name="cancellationToken">
    ///     An optional cancellation token to cancel the operation.
    /// </param>
    /// <returns>A <see cref="ValueTask{TResult}" /> containing the result of the publish operation.</returns>
    ValueTask<Result> EmitAsync<TEvent>(TEvent @event,
        EmitMode emitMode,
        CancellationToken cancellationToken = default)
        where TEvent : class, IEvent;

    /// <summary>
    ///     Publishes multiple events asynchronously in a batch operation.
    /// </summary>
    /// <typeparam name="TEvent">
    ///     The type of the events to be published. This must implement the <see cref="IEvent" /> interface.
    /// </typeparam>
    /// <param name="events">The collection of events to be published.</param>
    /// <param name="cancellationToken">An optional cancellation token to cancel the operation.</param>
    /// <returns>
    ///     A <see cref="ValueTask{TResult}" /> containing the result of the batch publish operation.
    ///     Returns success if all events were published successfully, or failure with details of any failures.
    /// </returns>
    ValueTask<Result> EmitBatchAsync<TEvent>(IEnumerable<TEvent> events,
        CancellationToken cancellationToken = default)
        where TEvent : class, IEvent;

    /// <summary>
    ///     Publishes multiple events asynchronously in a batch operation with the specified emit mode.
    /// </summary>
    /// <typeparam name="TEvent">
    ///     The type of the events to be published. This must implement the <see cref="IEvent" /> interface.
    /// </typeparam>
    /// <param name="events">The collection of events to be published.</param>
    /// <param name="emitMode">
    ///     Specifies the mode in which the events should be published, such as immediately (Now),
    ///     through an outbox mechanism, or using the default behavior.
    /// </param>
    /// <param name="cancellationToken">An optional cancellation token to cancel the operation.</param>
    /// <returns>
    ///     A <see cref="ValueTask{TResult}" /> containing the result of the batch publish operation.
    ///     Returns success if all events were published successfully, or failure with details of any failures.
    /// </returns>
    ValueTask<Result> EmitBatchAsync<TEvent>(IEnumerable<TEvent> events,
        EmitMode emitMode,
        CancellationToken cancellationToken = default)
        where TEvent : class, IEvent;
}