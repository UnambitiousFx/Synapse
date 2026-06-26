using UnambitiousFx.Functional;

namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Typed event pipeline behavior that only applies to a specific event type.
///     Registered by the DI container under <c>IEventPipelineBehavior&lt;TEvent&gt;</c> so only behaviors
///     declared for a given event type are resolved when dispatching that event.
/// </summary>
/// <typeparam name="TEvent">The event type this behavior handles.</typeparam>
public interface IEventPipelineBehavior<TEvent>
    where TEvent : IEvent
{
    /// <summary>
    ///     Handles the event within the pipeline for the specific <typeparamref name="TEvent" /> type.
    /// </summary>
    /// <param name="event">The event instance being processed.</param>
    /// <param name="next">Delegate to invoke the next behavior or the event handlers.</param>
    /// <param name="cancellationToken">Token for cancelling the operation.</param>
    /// <returns>A task containing the result of the operation.</returns>
    ValueTask<Result> HandleAsync(TEvent @event,
        EventHandlerDelegate<TEvent> next,
        CancellationToken cancellationToken = default);
}
