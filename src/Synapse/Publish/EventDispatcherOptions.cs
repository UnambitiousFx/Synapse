using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Publish;

/// <summary>
///     Configuration options for the EventDispatcher.
/// </summary>
internal sealed record EventDispatcherOptions
{
    /// <summary>
    ///     Strategy for dispatching events.
    /// </summary>
    public DispatchStrategy DispatchStrategy { get; set; } = DispatchStrategy.Immediate;

    /// <summary>
    ///     Dispatcher delegates for event types to support NativeAOT-friendly polymorphic dispatch.
    ///     These are registered at startup via source generation or explicit registration.
    ///     The delegate calls DispatchAsync with the correct generic type,
    ///     avoiding reflection and ensuring compatibility with NativeAOT and trimming.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         For NativeAOT compatibility, dispatchers should be registered at startup using one of two approaches:
    ///     </para>
    ///     <para>
    ///         <b>Option 1: Source-generated registration (recommended)</b>
    ///     </para>
    ///     <code>
    ///     // The Synapse.Generator emits RegisterGroup which implements both IRegisterGroup
    ///     // and IEventDispatcherRegistration. A single AddRegisterGroup call wires everything:
    ///     services.AddSynapse(cfg => cfg.AddRegisterGroup(new RegisterGroup()));
    ///     </code>
    ///     <para>
    ///         <b>Option 2: Manual registration</b>
    ///     </para>
    ///     <code>
    ///     services.Configure&lt;EventDispatcherOptions&gt;(options =>
    ///     {
    ///         options.Dispatchers[typeof(OrderCreatedEvent)] = (@event, dispatcher, ct) =>
    ///         {
    ///             var typedEvent = (OrderCreatedEvent)@event;
    ///             return dispatcher.DispatchAsync(typedEvent, ct);
    ///         };
    ///     });
    ///     </code>
    /// </remarks>
    public Dictionary<Type, DispatchEventDelegate> Dispatchers { get; set; } = new();
}

/// <summary>
///     Defines the strategy for dispatching events.
/// </summary>
public enum DispatchStrategy
{
    /// <summary>
    ///     Dispatch to handlers immediately.
    /// </summary>
    Immediate,

    /// <summary>
    ///     Store in outbox and defer dispatch to background processing.
    /// </summary>
    Deferred,

    /// <summary>
    ///     Accumulate events and dispatch in batches.
    /// </summary>
    Batched
}