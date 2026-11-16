using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Validation;

/// <summary>
///     Builds the per-request-type describer probes from the recorded request registrations, so a registration site
///     records its handler shape once. Events have no visitor method and keep their own probes.
/// </summary>
internal sealed class RequestProbeVisitor : IRequestRegistrationVisitor
{
    private readonly Dictionary<Type, Func<IPipelineDescriber, PipelineDescription?>> _probes;

    private RequestProbeVisitor(Dictionary<Type, Func<IPipelineDescriber, PipelineDescription?>> probes)
    {
        _probes = probes;
    }

    public static IReadOnlyDictionary<Type, Func<IPipelineDescriber, PipelineDescription?>> Collect(
        IReadOnlyDictionary<Type, Action<IRequestRegistrationVisitor>> visits,
        IReadOnlyDictionary<Type, Func<IPipelineDescriber, PipelineDescription?>> eventProbes)
    {
        var probes = new Dictionary<Type, Func<IPipelineDescriber, PipelineDescription?>>();
        var visitor = new RequestProbeVisitor(probes);
        foreach (var visit in visits.Values)
        {
            visit(visitor);
        }

        foreach (var (type, eventProbe) in eventProbes)
        {
            // A type that is both a request and an event needs both pipelines checked: one probe runs the two.
            probes[type] = probes.TryGetValue(type, out var requestProbe)
                ? describer =>
                {
                    var request = requestProbe(describer);
                    var @event = eventProbe(describer);
                    return request ?? @event;
                }
                : eventProbe;
        }

        return probes;
    }

    // A request type with several handler shapes keeps the first one recorded, as the per-type probes always did.
    public void VisitRequest<TRequest>()
        where TRequest : IRequest
    {
        _probes.TryAdd(typeof(TRequest), describer => describer.Describe<TRequest>());
    }

    public void VisitRequest<TRequest, TResponse>()
        where TRequest : IRequest<TResponse>
        where TResponse : notnull
    {
        _probes.TryAdd(typeof(TRequest), describer => describer.Describe<TRequest, TResponse>());
    }

    // Streams were never probed: the describer has no stream overload.
    public void VisitStreamRequest<TRequest, TItem>()
        where TRequest : IStreamRequest<TItem>
        where TItem : notnull
    {
    }
}
