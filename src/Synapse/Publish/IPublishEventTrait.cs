using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Publish;

internal interface IPublishEventTrait
{
    Type EventType { get; }
    DistributionMode DistributionMode { get; }
}