using UnambitiousFx.Functional;

namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Defines an interface for implementing a pipeline behavior that can be executed
///     around the handling of events in an event-driven mediator pattern.
/// </summary>
public interface IEventPipelineBehavior
{
    /// Handles the specified event by invoking the next delegate in the event pipeline behavior chain.
    /// <typeparam name="TEvent">The type of the event being handled. Must implement the <see cref="IEvent" /> interface.</typeparam>
    /// <param name="event">
    ///     The event instance that is being processed by the pipeline behavior.
    /// </param>
    /// <param name="next">
    ///     A delegate that points to the next behavior in the pipeline, or the final event handler if this is the last
    ///     behavior.
    /// </param>
    /// <param name="cancellationToken">
    ///     A token that can be used to propagate notification of cancellation.
    /// </param>
    /// <returns>
    ///     A <see cref="ValueTask{Result}" /> representing the result of this pipeline behavior and subsequent behaviors or
    ///     handlers.
    ///     If successful, the result will indicate the successful processing of the event; otherwise, it will indicate a
    ///     failure.
    /// </returns>
    ValueTask<Result> HandleAsync<TEvent>(TEvent @event,
        EventHandlerDelegate next,
        CancellationToken cancellationToken = default)
        where TEvent : IEvent;
}