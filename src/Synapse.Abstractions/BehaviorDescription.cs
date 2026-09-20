namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     One pipeline behavior in a <see cref="PipelineDescription" />.
/// </summary>
/// <param name="Type">The concrete behavior type that runs, e.g. a closed generic such as <c>AuditBehavior&lt;CreateTask&gt;</c>.</param>
/// <param name="Order">
///     The position the behavior declared through <see cref="IOrderedPipelineBehavior" />, or
///     <see cref="IOrderedPipelineBehavior.Last" /> when it declares none.
/// </param>
public sealed record BehaviorDescription(Type Type, uint Order);
