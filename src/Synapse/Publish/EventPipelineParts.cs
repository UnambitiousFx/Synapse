using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Pipelines;
using UnambitiousFx.Synapse.Resolvers;

namespace UnambitiousFx.Synapse.Publish;

/// <summary>
///     Resolves what an event's pipeline is made of. Shared by the dispatcher, which composes it, and the pipeline
///     describer, which reports it, so the two cannot disagree.
/// </summary>
internal static class EventPipelineParts
{
    /// <summary>
    ///     Resolves the handlers subscribed to <typeparamref name="TEvent" /> and the behaviors declared for exactly
    ///     that type, the behaviors ordered by runtime pipeline position. The stable sort keeps registration order
    ///     for behaviors that share an <c>Order</c>.
    /// </summary>
    public static (IEventHandler<TEvent>[] Handlers, IEventPipelineBehavior<TEvent>[] Behaviors) Resolve<TEvent>(
        IDependencyResolver resolver)
        where TEvent : class, IEvent
    {
        var resolved = resolver.GetServices<IEventHandler<TEvent>>();
        var handlers = resolved as IEventHandler<TEvent>[] ?? resolved.ToArray();

        var behaviors = resolver.GetServices<IEventPipelineBehavior<TEvent>>()
            .OrderBy(PipelineBehaviorOrdering.OrderOf)
            .ToArray();

        return (handlers, behaviors);
    }
}
