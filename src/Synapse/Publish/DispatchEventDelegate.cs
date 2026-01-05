using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Publish;

/// <summary>
///     Delegate for dispatching events from the outbox without reflection.
///     This delegate maintains generic type information through closures, enabling NativeAOT compatibility.
/// </summary>
/// <param name="event">The event to dispatch.</param>
/// <param name="dispatcher">The event dispatcher instance.</param>
/// <param name="cancellationToken">A cancellation token to observe.</param>
/// <returns>A ValueTask containing the result of the dispatch operation.</returns>
/// <remarks>
///     This delegate is used by the OutboxManager to replay events from the outbox without using reflection.
///     Each event type has a registered dispatcher that casts the event to the correct type and calls
///     the generic DispatchFromOutboxAsync method, preserving type information for NativeAOT compilation.
/// </remarks>
internal delegate ValueTask<Result> DispatchEventDelegate(
    IEvent @event,
    IEventDispatcher dispatcher,
    CancellationToken cancellationToken);