using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Tests.Definitions;

/// <summary>Generic test behavior for events.</summary>
public sealed class TestEventPipelineBehavior<TEvent> : IEventPipelineBehavior<TEvent>
    where TEvent : IEvent
{
    public bool Executed { get; private set; }
    public object? EventExecuted { get; private set; }
    public int ExecutionCount { get; private set; }
    public Action? OnExecuted { get; set; }

    public ValueTask<Result> HandleAsync(TEvent @event,
        EventHandlerDelegate<TEvent> next,
        CancellationToken cancellationToken = default)
    {
        Executed = true;
        EventExecuted = @event;
        ExecutionCount++;
        OnExecuted?.Invoke();
        return next(@event, cancellationToken);
    }
}
