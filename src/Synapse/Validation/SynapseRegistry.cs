using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Validation;

/// <summary>
///     What <c>AddSynapse</c> registered, captured for <c>ValidateSynapse</c>.
/// </summary>
/// <remarks>
///     Handler and behavior facts come from a snapshot of the <see cref="ServiceDescriptor" />s taken when
///     <c>AddSynapse</c> finishes, so a behavior added to the collection afterwards is not seen. Only descriptors
///     whose service type is an already-closed generic are read, through <see cref="Type.GetGenericArguments" />;
///     nothing is constructed by reflection, which keeps this Native-AOT safe. Open-generic behavior descriptors are
///     skipped: DI closes them lazily over any request, so which requests they apply to cannot be known here.
///     Probes are recorded by the closed <c>Register…Handler&lt;…&gt;</c> methods for the same reason.
/// </remarks>
internal sealed class SynapseRegistry
{
    private SynapseRegistry(
        IReadOnlyDictionary<Type, Func<IPipelineDescriber, PipelineDescription?>> probes,
        IReadOnlyDictionary<Type, int> requestHandlerCounts,
        IReadOnlySet<Type> eventsWithHandlers,
        IReadOnlySet<Type> requestsWithBehaviors,
        IReadOnlySet<Type> eventsWithBehaviors)
    {
        Probes = probes;
        RequestHandlerCounts = requestHandlerCounts;
        EventsWithHandlers = eventsWithHandlers;
        RequestsWithBehaviors = requestsWithBehaviors;
        EventsWithBehaviors = eventsWithBehaviors;
    }

    /// <summary>Describes the pipeline of one request or event type, keyed by that type.</summary>
    public IReadOnlyDictionary<Type, Func<IPipelineDescriber, PipelineDescription?>> Probes { get; }

    /// <summary>The number of handler descriptors registered for each request type.</summary>
    public IReadOnlyDictionary<Type, int> RequestHandlerCounts { get; }

    /// <summary>Event types with at least one handler descriptor.</summary>
    public IReadOnlySet<Type> EventsWithHandlers { get; }

    /// <summary>Request types a closed request behavior is registered for.</summary>
    public IReadOnlySet<Type> RequestsWithBehaviors { get; }

    /// <summary>Event types a closed event behavior is registered for.</summary>
    public IReadOnlySet<Type> EventsWithBehaviors { get; }

    public static SynapseRegistry Create(
        IServiceCollection services,
        IReadOnlyDictionary<Type, Func<IPipelineDescriber, PipelineDescription?>> probes)
    {
        var requestHandlerCounts = new Dictionary<Type, int>();
        var eventsWithHandlers = new HashSet<Type>();
        var requestsWithBehaviors = new HashSet<Type>();
        var eventsWithBehaviors = new HashSet<Type>();

        foreach (var descriptor in services)
        {
            var serviceType = descriptor.ServiceType;
            if (!serviceType.IsGenericType || serviceType.ContainsGenericParameters)
            {
                continue;
            }

            var definition = serviceType.GetGenericTypeDefinition();
            var target = serviceType.GetGenericArguments()[0];

            if (definition == typeof(IRequestHandler<>) || definition == typeof(IRequestHandler<,>))
            {
                requestHandlerCounts[target] = requestHandlerCounts.GetValueOrDefault(target) + 1;
            }
            else if (definition == typeof(IEventHandler<>))
            {
                eventsWithHandlers.Add(target);
            }
            else if (definition == typeof(IRequestPipelineBehavior<>) ||
                     definition == typeof(IRequestPipelineBehavior<,>))
            {
                requestsWithBehaviors.Add(target);
            }
            else if (definition == typeof(IEventPipelineBehavior<>))
            {
                eventsWithBehaviors.Add(target);
            }
        }

        return new SynapseRegistry(probes, requestHandlerCounts, eventsWithHandlers, requestsWithBehaviors,
            eventsWithBehaviors);
    }
}
