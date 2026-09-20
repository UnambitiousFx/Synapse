namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     The handler(s) and behaviors a message type resolves to, as the dispatcher runs them.
/// </summary>
/// <param name="Handlers">
///     The handler types: exactly one for a request, one per subscriber for an event.
/// </param>
/// <param name="Behaviors">
///     The behaviors in execution order, outermost first. Behaviors that share an <c>Order</c> keep their
///     registration order.
/// </param>
public sealed record PipelineDescription(
    IReadOnlyList<Type> Handlers,
    IReadOnlyList<BehaviorDescription> Behaviors);
