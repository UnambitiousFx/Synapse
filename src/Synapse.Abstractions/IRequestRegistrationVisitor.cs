namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Receives every request type Synapse registers, with its closed generic arguments, so a
///     package can register closed generic services (NativeAOT-friendly) instead of open generics.
/// </summary>
public interface IRequestRegistrationVisitor
{
    /// <summary>Called once for each registered request without a response.</summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    void VisitRequest<TRequest>()
        where TRequest : IRequest;

    /// <summary>Called once for each registered request with a response.</summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    void VisitRequest<TRequest, TResponse>()
        where TRequest : IRequest<TResponse>
        where TResponse : notnull;

    /// <summary>Called once for each registered stream request.</summary>
    /// <typeparam name="TRequest">The stream request type.</typeparam>
    /// <typeparam name="TItem">The streamed item type.</typeparam>
    void VisitStreamRequest<TRequest, TItem>()
        where TRequest : IStreamRequest<TItem>
        where TItem : notnull;
}
