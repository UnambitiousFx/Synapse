namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Marks a class as a pipeline behavior that should be automatically registered by the source generator.
///     The generator scans for this attribute, determines the behavior kind and arity from the implemented interface,
///     and emits closed-generic registrations for every matching handler in the assembly.
///     Open-generic behaviors (e.g. <c>LoggingBehavior&lt;TRequest, TResponse&gt;</c>) are cross-producted with all
///     discovered handlers whose request type satisfies the behavior's generic constraints.
/// </summary>
/// <remarks>
///     <para>
///         Closed behaviors (implementing e.g. <see cref="IRequestPipelineBehavior{TRequest,TResponse}" /> with concrete
///         type args) are emitted as a single registration for that exact type pair.
///     </para>
///     <para>
///         Pipeline position comes from <see cref="Stage" />, <see cref="Before" /> and <see cref="After" /> when they
///         are set, then from <see cref="IOrderedPipelineBehavior.Order" />, then from registration order. The
///         generator reads these properties and emits the matching ordering registration; without the generator,
///         declare the same thing with <c>ISynapseConfig.ConfigureBehaviorOrdering</c>, since the attribute alone is
///         not read at runtime.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     [PipelineBehavior(Stage = PipelineStage.Authorization, After = typeof(ActorBehavior&lt;,&gt;))]
///     public sealed class AuditBehavior&lt;TRequest, TResponse&gt; : IRequestPipelineBehavior&lt;TRequest, TResponse&gt; { ... }
///     </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class PipelineBehaviorAttribute : Attribute
{
    private PipelineStage? _stage;

    /// <summary>
    ///     The stage the behavior runs in. Unset by default: the behavior then belongs to the stage whose
    ///     <see cref="PipelineStages" /> band contains its <see cref="IOrderedPipelineBehavior.Order" />.
    /// </summary>
    public PipelineStage Stage
    {
        get => _stage ?? PipelineStages.Of(IOrderedPipelineBehavior.Last);
        set => _stage = value;
    }

    /// <summary>Whether <see cref="Stage" /> was set explicitly.</summary>
    public bool HasStage => _stage.HasValue;

    /// <summary>
    ///     A behavior this one must run before (outside of). Pass the open-generic definition, e.g.
    ///     <c>typeof(ScopeBehavior&lt;,&gt;)</c>, to match every closed form, or a concrete type to match only it.
    ///     Ignored for pipelines that do not contain that behavior.
    /// </summary>
    public Type? Before { get; set; }

    /// <summary>
    ///     A behavior this one must run after (inside of). Matches like <see cref="Before" />.
    /// </summary>
    public Type? After { get; set; }
}
