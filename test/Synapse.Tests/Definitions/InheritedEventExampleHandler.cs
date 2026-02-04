using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Tests.Definitions;

public sealed class InheritedEventExampleHandler : IEventHandler<InheritedEventExample>
{
    public bool Executed { get; private set; }
    public InheritedEventExample? EventExecuted { get; private set; }
    public int ExecutionCount { get; private set; }
    public Action? OnExecuted { get; set; }

    public ValueTask<Result> HandleAsync(InheritedEventExample @event,
        CancellationToken cancellationToken = default)
    {
        Executed = true;
        EventExecuted = @event;
        ExecutionCount++;
        OnExecuted?.Invoke();
        return new ValueTask<Result>(Result.Success());
    }
}