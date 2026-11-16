namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Receives every request type Synapse registers, with its closed generic arguments, so a
///     package can register closed generic services (NativeAOT-friendly) instead of open generics.
/// </summary>
/// <remarks>
///     Each method is called once per registered handler shape and per visitor, synchronously: inside
///     <c>AddRequestRegistrationVisitor</c> for the shapes already registered (replay), or inside <c>AddSynapse</c>
///     for the shapes registered later.
/// </remarks>
public interface IRequestRegistrationVisitor
{
    /// <summary>Called for each registered request without a response handler shape.</summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    void VisitRequest<TRequest>()
        where TRequest : IRequest;

    /// <summary>Called for each registered request with a response handler shape.</summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    void VisitRequest<TRequest, TResponse>()
        where TRequest : IRequest<TResponse>
        where TResponse : notnull;

    /// <summary>Called for each registered stream request handler shape.</summary>
    /// <typeparam name="TRequest">The stream request type.</typeparam>
    /// <typeparam name="TItem">The streamed item type.</typeparam>
    void VisitStreamRequest<TRequest, TItem>()
        where TRequest : IStreamRequest<TItem>
        where TItem : notnull;
}
