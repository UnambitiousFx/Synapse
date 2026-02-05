using UnambitiousFx.Functional;

namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Represents a publisher capable of handling and publishing events to their respective subscribers or processors.
/// </summary>
public interface IPublisher
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
    ValueTask<Result> PublishAsync<TEvent>(TEvent @event,
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
    /// <param name="publishMode">
    ///     Specifies the mode in which the event should be published, such as immediately (Now),
    ///     through an outbox mechanism, or using the default behavior.
    /// </param>
    /// <param name="distributionMode">
    ///     Specifies the distribution mode for the event, such as Local, External, or LocalAndExternal.
    /// </param>
    /// <param name="cancellationToken">
    ///     An optional cancellation token to cancel the operation.
    /// </param>
    /// <returns>A <see cref="ValueTask{TResult}" /> containing the result of the publish operation.</returns>
    ValueTask<Result> PublishAsync<TEvent>(TEvent @event,
        PublishMode publishMode,
        DistributionMode distributionMode,
        CancellationToken cancellationToken = default)
        where TEvent : class, IEvent;
}