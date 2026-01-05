using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Publish.Orchestrators;

/// <summary>
///     Represents an event orchestrator responsible for executing event handlers in a coordinated manner.
/// </summary>
public interface IEventOrchestrator
{
    /// Executes asynchronous handling of an event using the provided handlers.
    /// <typeparam name="TEvent">The type of the event to be handled.</typeparam>
    /// <param name="handlers">A collection of handlers responsible for managing the event.</param>
    /// <param name="event">The event instance to be handled.</param>
    /// <param name="cancellationToken">An optional CancellationToken to signal cancellation of the operation.</param>
    /// <returns>
    ///     A task that represents the asynchronous operation, containing a Result indicating the outcome of the event
    ///     handling process.
    /// </returns>
    ValueTask<Result> RunAsync<TEvent>(IEnumerable<IEventHandler<TEvent>> handlers,
        TEvent @event,
        CancellationToken cancellationToken = default)
        where TEvent : class, IEvent;
}