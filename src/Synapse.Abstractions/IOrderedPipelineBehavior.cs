namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Opt-in contract that lets a pipeline behavior declare its runtime position in the pipeline
///     chain. Behaviors are ordered globally by <see cref="Order" /> (lower runs first / outermost),
///     independent of how or where they were registered. A behavior that does not implement this
///     interface is treated as <see cref="Last" /> (innermost). Ordering is stable: behaviors that
///     share an <see cref="Order" /> keep their registration order.
/// </summary>
public interface IOrderedPipelineBehavior
{
    /// <summary>
    ///     The position of this behavior in the pipeline chain. Lower values run first (outermost),
    ///     higher values run last (innermost, closest to the handler).
    /// </summary>
    uint Order { get; }

    /// <summary>The outermost position — runs before every other behavior.</summary>
    static uint First => uint.MinValue;

    /// <summary>The midpoint position, for behaviors that should sit between <see cref="First" /> and <see cref="Last" />.</summary>
    static uint Middle => uint.MaxValue / 2;

    /// <summary>The innermost position — runs after every other behavior, closest to the handler.</summary>
    static uint Last => uint.MaxValue;
}
