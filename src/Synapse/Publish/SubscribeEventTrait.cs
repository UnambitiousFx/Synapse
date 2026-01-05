using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Publish;

internal abstract class SubscribeEventTrait : ISubscribeEventTrait
{
    public abstract Type EventType { get; }
    public int MaxConcurrency { get; init; }
    public abstract ValueTask<Result> HandleAsync(object @event, CancellationToken cancellationToken);
}

internal sealed class SubscribeEventTrait<TEvent> : SubscribeEventTrait where TEvent : class, IEvent
{
    private readonly IEventHandler<TEvent> _handler;

    public SubscribeEventTrait(IEventHandler<TEvent> handler)
    {
        _handler = handler;
    }

    public override Type EventType => typeof(TEvent);

    public override ValueTask<Result> HandleAsync(object @event, CancellationToken cancellationToken)
    {
        if (@event is not TEvent typedEvent) throw new ArgumentException($"Event must be of type {typeof(TEvent)}");

        return _handler.HandleAsync(typedEvent, cancellationToken);
    }
}