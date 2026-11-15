using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.FluentValidation;

/// <summary>
///     Options for the FluentValidation request behaviors registered by
///     <see cref="ServiceCollectionExtensions.AddSynapseFluentValidation" />.
/// </summary>
public sealed class FluentValidationBehaviorOptions
{
    /// <summary>
    ///     Position of the validation behavior in the request pipeline (see <see cref="IOrderedPipelineBehavior" />).
    ///     Defaults to the start of <see cref="PipelineStage.Validation" />: after authorization, before transactions,
    ///     so an invalid request is rejected before a transaction opens.
    /// </summary>
    public uint Order { get; set; } = PipelineStages.StartOf(PipelineStage.Validation);
}
