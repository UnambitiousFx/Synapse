namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     One pipeline behavior in a <see cref="PipelineDescription" />.
/// </summary>
public sealed record BehaviorDescription
{
    /// <summary>
    ///     Describes a behavior that declares no stage: its <see cref="Stage" /> is the one whose band contains
    ///     <paramref name="order" /> (see <see cref="PipelineStages.Of" />).
    /// </summary>
    /// <param name="type">The concrete behavior type that runs.</param>
    /// <param name="order">The behavior's <see cref="IOrderedPipelineBehavior.Order" />, or <see cref="IOrderedPipelineBehavior.Last" />.</param>
    public BehaviorDescription(Type type, uint order)
        : this(type, order, PipelineStages.Of(order))
    {
    }

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
    /// <param name="type">The behavior type.</param>
    /// <param name="order">The behavior's order.</param>
    public void Deconstruct(out Type type, out uint order)
    {
        type = Type;
        order = Order;
    }
}
