using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Pipelines;

/// <summary>
///     One ordering declaration for a behavior type, from <c>ConfigureBehaviorOrdering</c> or the source generator.
///     Registered as a singleton per declaration and merged by <see cref="PipelineOrdering" />.
/// </summary>
internal sealed record BehaviorOrderingRule(Type BehaviorType, PipelineStage? Stage, Type? Before, Type? After)
{
    /// <summary>Validates the arguments of <c>ConfigureBehaviorOrdering</c> and builds the rule.</summary>
    public static BehaviorOrderingRule Create(Type behaviorType, PipelineStage? stage, Type? before, Type? after)
    {
        ArgumentNullException.ThrowIfNull(behaviorType);

        if (stage is null && before is null && after is null)
        {
            throw new ArgumentException(
                $"Declare a stage, a Before or an After for '{behaviorType}'; nothing was declared.",
                nameof(behaviorType));
        }

        if (stage is { } declared && !Enum.IsDefined(declared))
        {
            throw new ArgumentException($"'{declared}' is not a defined pipeline stage.", nameof(stage));
        }

        if (before is not null && PipelineOrdering.Matches(behaviorType, before))
        {
            throw new ArgumentException($"'{behaviorType}' cannot run before itself.", nameof(before));
        }

        if (after is not null && PipelineOrdering.Matches(behaviorType, after))
        {
            throw new ArgumentException($"'{behaviorType}' cannot run after itself.", nameof(after));
        }

        return new BehaviorOrderingRule(behaviorType, stage, before, after);
    }
}
