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
    ///     Defaults to <see cref="IOrderedPipelineBehavior.Last" />, closest to the handler, like the built-in
    ///     <c>RequestValidationBehavior</c>.
    /// </summary>
    public uint Order { get; set; } = IOrderedPipelineBehavior.Last;
}
