using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Publish.Orchestrators;

/// Represents an orchestrator responsible for executing event handlers sequentially.
/// This class is specifically designed to handle events by invoking each corresponding
/// event handler in sequence. It ensures that every handler processes the event in the
/// specified order, and their individual results are aggregated into a single final result.
public sealed class SequentialEventOrchestrator : IEventOrchestrator
{
    /// <inheritdoc />
    public async ValueTask<Result> RunAsync<TEvent>(IEnumerable<IEventHandler<TEvent>> handlers,
        TEvent @event,
        CancellationToken cancellationToken = default)
        where TEvent : class, IEvent
    {
        var results = new List<Result>();
        foreach (var eventHandler in handlers)
        {
            var result = await eventHandler.HandleAsync(@event, cancellationToken);
            results.Add(result);
        }

        return results.Combine();
    }
}