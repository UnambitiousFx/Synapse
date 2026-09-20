using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Pipelines;

/// <summary>
///     Implemented by the request proxies so a description is read from the chain the dispatcher actually runs,
///     instead of being computed a second time.
/// </summary>
internal interface IPipelineInfo
{
    Type HandlerType { get; }

    IReadOnlyList<BehaviorDescription> Behaviors { get; }
}
