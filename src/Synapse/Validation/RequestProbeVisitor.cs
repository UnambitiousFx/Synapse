using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Validation;

/// <summary>
///     Builds the per-request-type describer probes from the recorded request registrations, so a registration site
///     records its handler shape once. Events have no visitor method and keep their own probes, in a separate table
///     so a type that is both a request and an event never has one kind answer for the other.
/// </summary>
internal sealed class RequestProbeVisitor : IRequestRegistrationVisitor
{
    private readonly Dictionary<Type, Func<IPipelineDescriber, PipelineDescription?>> _probes;
    private readonly HashSet<Type> _typed = [];

    private RequestProbeVisitor(Dictionary<Type, Func<IPipelineDescriber, PipelineDescription?>> probes)
    {
        _probes = probes;
    }

    public static IReadOnlyDictionary<Type, Func<IPipelineDescriber, PipelineDescription?>> Collect(
        IReadOnlyDictionary<Type, Action<IRequestRegistrationVisitor>> visits)
    {
        var probes = new Dictionary<Type, Func<IPipelineDescriber, PipelineDescription?>>();
        var visitor = new RequestProbeVisitor(probes);
        foreach (var visit in visits.Values)
        {
            visit(visitor);
        }

        return probes;
    }

    // A request type with several handler shapes keeps the IRequest<TResponse> one, as Describe(Type) always did;
    // among several IRequest<T> closures the first recorded wins.
    public void VisitRequest<TRequest>()
        where TRequest : IRequest
    {
        _probes.TryAdd(typeof(TRequest), describer => describer.Describe<TRequest>());
    }

    public void VisitRequest<TRequest, TResponse>()
        where TRequest : IRequest<TResponse>
        where TResponse : notnull
    {
        if (_typed.Add(typeof(TRequest)))
        {
            _probes[typeof(TRequest)] = describer => describer.Describe<TRequest, TResponse>();
        }
    }

    // Streams were never probed: the describer has no stream overload.
    public void VisitStreamRequest<TRequest, TItem>()
        where TRequest : IStreamRequest<TItem>
        where TItem : notnull
    {
    }
}
