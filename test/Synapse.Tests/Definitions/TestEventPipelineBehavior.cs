using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Tests.Definitions;

public sealed class TestEventPipelineBehavior : IEventPipelineBehavior
{
    public bool Executed { get; private set; }
    public object? EventExecuted { get; private set; }
    public int ExecutionCount { get; private set; }
    public Action? OnExecuted { get; set; }

    public ValueTask<Result> HandleAsync<TEvent>(TEvent @event,
        EventHandlerDelegate next,
        CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        Executed = true;
        EventExecuted = @event;
        ExecutionCount++;
        OnExecuted?.Invoke();
        return next();
    }
}