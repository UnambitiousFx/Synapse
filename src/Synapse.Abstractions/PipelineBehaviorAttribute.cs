namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Marks a class as a pipeline behavior that should be automatically registered by the source generator.
///     The generator scans for this attribute, determines the behavior kind and arity from the implemented interface,
///     and emits closed-generic registrations for every matching handler in the assembly.
///     Open-generic behaviors (e.g. <c>LoggingBehavior&lt;TRequest, TResponse&gt;</c>) are cross-producted with all
///     discovered handlers whose request type satisfies the behavior's generic constraints.
/// </summary>
/// <remarks>
///     Closed behaviors (implementing e.g. <see cref="IRequestPipelineBehavior{TRequest,TResponse}" /> with concrete
///     type args) are emitted as a single registration for that exact type pair.
/// </remarks>
/// <remarks>
///     Pipeline position is controlled at runtime by implementing <see cref="IOrderedPipelineBehavior" />,
///     not by this attribute, which serves only as the discovery marker for the source generator.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class PipelineBehaviorAttribute : Attribute;
