using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Publish.Orchestrators;

/// <summary>
///     An orchestrator that concurrently executes multiple event handlers for a specified event type.
/// </summary>
/// <remarks>
///     The <see cref="ConcurrentEventOrchestrator" /> is responsible for running all event handlers concurrently for
///     a given event.
/// </remarks>
public sealed class ConcurrentEventOrchestrator : IEventOrchestrator
{
    /// <inheritdoc />
    public ResultTask RunAsync<TEvent>(IEnumerable<IEventHandler<TEvent>> handlers,
        TEvent @event,
        CancellationToken cancellationToken = default)
        where TEvent : class, IEvent
    {
        return RunCoreAsync(handlers, @event, cancellationToken).AsAsync();
    }

    private async ValueTask<Result> RunCoreAsync<TEvent>(IEnumerable<IEventHandler<TEvent>> handlers,
        TEvent @event,
        CancellationToken cancellationToken = default)
        where TEvent : class, IEvent
    {
        var tasks = handlers.Select(eventHandler => eventHandler.HandleAsync(@event, cancellationToken).AsTask())
            .ToList();

        var results = await Task.WhenAll(tasks);
        return results.Combine();
    }
}