using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Publish;

/// <summary>
///     Configuration options for the EventDispatcher.
/// </summary>
internal sealed record EventDispatcherOptions
{
    /// <summary>
    ///     Default distribution mode when no routing filter or message traits specify otherwise.
    /// </summary>
    public DistributionMode DefaultDistributionMode { get; set; } = DistributionMode.Local;

    /// <summary>
    ///     Strategy for dispatching external events.
    /// </summary>
    public DispatchStrategy DispatchStrategy { get; set; } = DispatchStrategy.Immediate;


    /// <summary>
    ///     Dispatcher delegates for event types to support NativeAOT-friendly outbox replay.
    ///     These are registered at startup via source generation or explicit registration.
    ///     The delegate signature calls DispatchFromOutboxAsync with the correct generic type,
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
    ///     // The Synapse.Generator will generate this code for all IEvent types
    ///     public static class GeneratedEventDispatchers
    ///     {
    ///         public static void RegisterDispatchers(EventDispatcherOptions options)
    ///         {
    ///             options.Dispatchers[typeof(OrderCreatedEvent)] = async (@event, dispatcher, distributionMode, ct) =>
    ///             {
    ///                 var typedEvent = (OrderCreatedEvent)@event;
    ///                 return await dispatcher.DispatchFromOutboxAsync(typedEvent, distributionMode, ct);
    ///             };
    ///             // ... generated for all event types
    ///         }
    ///     }
    ///     </code>
    ///     <para>
    ///         <b>Option 2: Manual registration</b>
    ///     </para>
    ///     <code>
    ///     services.Configure&lt;EventDispatcherOptions&gt;(options =>
    ///     {
    ///         options.Dispatchers[typeof(OrderCreatedEvent)] = async (@event, dispatcher, distributionMode, ct) =>
    ///         {
    ///             var typedEvent = (OrderCreatedEvent)@event;
    ///             return await dispatcher.DispatchFromOutboxAsync(typedEvent, distributionMode, ct);
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
    ///     Dispatch to transport immediately after storing in outbox.
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