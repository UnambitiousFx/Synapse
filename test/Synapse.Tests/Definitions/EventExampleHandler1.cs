using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Tests.Definitions;

public sealed class EventExampleHandler1 : IEventHandler<EventExample>
{
    public bool Executed { get; private set; }
    public EventExample? EventExecuted { get; private set; }
    public int ExecutionCount { get; private set; }
    public Action? OnExecuted { get; set; }

    public ValueTask<Result> HandleAsync(EventExample @event,
        CancellationToken cancellationToken = default)
    {
        Executed = true;
        EventExecuted = @event;
        ExecutionCount++;
        OnExecuted?.Invoke();
        return new ValueTask<Result>(Result.Success());
    }
}