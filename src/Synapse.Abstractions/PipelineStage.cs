namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Well-known pipeline stages, from outermost to innermost. A behavior that declares a stage, through
///     <see cref="PipelineBehaviorAttribute.Stage" /> or a runtime ordering registration, runs after every behavior
///     of an earlier stage and before every behavior of a later stage, whatever its <see cref="IOrderedPipelineBehavior.Order" />.
/// </summary>
/// <remarks>
///     Each stage also owns a band of <see cref="IOrderedPipelineBehavior.Order" /> values (see
///     <see cref="PipelineStages" />). A behavior that declares no stage belongs to the stage whose band contains
///     its <c>Order</c>, so behaviors that only set <c>Order</c> keep the order they always had, and they interleave
///     with staged behaviors predictably.
/// </remarks>
public enum PipelineStage
{
    /// <summary>Tracing, logging and metrics: wraps everything else. Holds <see cref="IOrderedPipelineBehavior.First" />.</summary>
    Observability = 0,

    /// <summary>Establishes who is calling.</summary>
    Authentication = 1,

    /// <summary>Decides whether the caller may run the request.</summary>
    Authorization = 2,

    /// <summary>Rejects invalid requests before any side effect. Holds <see cref="IOrderedPipelineBehavior.Middle" />.</summary>
    Validation = 3,

    /// <summary>Unit of work and transactions around the handler.</summary>
    Transaction = 4,

    /// <summary>Closest to the handler. Holds <see cref="IOrderedPipelineBehavior.Last" />, the default of a behavior without <c>Order</c>.</summary>
    Handler = 5
}
