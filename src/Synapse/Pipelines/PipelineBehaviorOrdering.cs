using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Pipelines;

/// <summary>
///     Resolves the runtime pipeline position of a behavior. Behaviors that implement
///     <see cref="IOrderedPipelineBehavior" /> contribute their declared <see cref="IOrderedPipelineBehavior.Order" />;
///     all others default to <see cref="IOrderedPipelineBehavior.Last" /> (innermost). Used as a stable
///     <c>OrderBy</c> key at every pipeline entry point so ordering is independent of registration source, and as the
///     tie-breaker of <see cref="PipelineOrdering" /> when stages or constraints are declared.
/// </summary>
internal static class PipelineBehaviorOrdering
{
    public static uint OrderOf(object behavior)
    {
        return behavior is IOrderedPipelineBehavior ordered
            ? ordered.Order
            : IOrderedPipelineBehavior.Last;
    }

    /// <summary>
    ///     Reports already-sorted behaviors as their concrete type, declared order and stage, for
    ///     <see cref="IPipelineDescriber" />.
    /// </summary>
    public static IReadOnlyList<BehaviorDescription> Describe(IReadOnlyList<object> sortedBehaviors,
        PipelineOrdering ordering)
    {
        var described = new BehaviorDescription[sortedBehaviors.Count];
        for (var i = 0; i < described.Length; i++)
        {
            var behavior = sortedBehaviors[i];
            described[i] = new BehaviorDescription(behavior.GetType(), OrderOf(behavior), ordering.StageOf(behavior));
        }

        return described;
    }
}
