using UnambitiousFx.Examples.ConsoleApp.Events;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.ConsoleApp.Handlers.Events;

[EventHandler<MetricsEvent>]
public class MetricsEventHandler : IEventHandler<MetricsEvent>
{
    public ValueTask<Result> HandleAsync(MetricsEvent @event,
        CancellationToken cancellationToken = default)
    {
        // Minimal processing for high-volume metrics
        return ValueTask.FromResult(Result.Success());
    }
}