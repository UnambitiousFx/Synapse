using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Publish;

internal sealed record PublishEventTrait<TEvent> : PublishEventTrait where TEvent : class, IEvent
{
    public override Type EventType => typeof(TEvent);
}

internal abstract record PublishEventTrait : IPublishEventTrait
{
    public DistributionMode DistributionMode { get; init; } = DistributionMode.Local;
    public abstract Type EventType { get; }
}