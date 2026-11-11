namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     One pipeline behavior in a <see cref="PipelineDescription" />.
/// </summary>
public sealed record BehaviorDescription
{
    /// <summary>
    ///     Describes a behavior that declares no stage: its <see cref="Stage" /> is the one whose band contains
    ///     <paramref name="Order" /> (see <see cref="PipelineStages.Of" />).
    /// </summary>
    /// <param name="Type">The concrete behavior type that runs.</param>
    /// <param name="Order">The behavior's <see cref="IOrderedPipelineBehavior.Order" />, or <see cref="IOrderedPipelineBehavior.Last" />.</param>
    /// <remarks>
    ///     The parameters keep the PascalCase names of the positional record this type used to be, so callers that
    ///     pass them by name keep compiling.
    /// </remarks>
    // ReSharper disable InconsistentNaming
    public BehaviorDescription(Type Type, uint Order)
        : this(Type, Order, PipelineStages.Of(Order))
    {
    }
    // ReSharper restore InconsistentNaming

    /// <summary>
    ///     Describes a behavior with the stage it runs in.
    /// </summary>
    /// <param name="type">The concrete behavior type that runs.</param>
    /// <param name="order">The behavior's <see cref="IOrderedPipelineBehavior.Order" />, or <see cref="IOrderedPipelineBehavior.Last" />.</param>
    /// <param name="stage">The stage the behavior runs in, declared or implied by <paramref name="order" />.</param>
    public BehaviorDescription(Type type, uint order, PipelineStage stage)
    {
        Type = type;
        Order = order;
        Stage = stage;
    }

    /// <summary>The concrete behavior type that runs, e.g. a closed generic such as <c>AuditBehavior&lt;CreateTask&gt;</c>.</summary>
    public Type Type { get; init; }

    /// <summary>
    ///     The position the behavior declared through <see cref="IOrderedPipelineBehavior" />, or
    ///     <see cref="IOrderedPipelineBehavior.Last" /> when it declares none.
    /// </summary>
    public uint Order { get; init; }

    /// <summary>
    ///     The stage the behavior runs in: the one it declared, or the one whose band contains its <see cref="Order" />.
    /// </summary>
    public PipelineStage Stage { get; init; }

    /// <summary>Deconstructs the type and order, like the positional record this type used to be.</summary>
    /// <param name="Type">The behavior type.</param>
    /// <param name="Order">The behavior's order.</param>
    // ReSharper disable InconsistentNaming
    public void Deconstruct(out Type Type, out uint Order)
    {
        Type = this.Type;
        Order = this.Order;
    }
    // ReSharper restore InconsistentNaming
}
