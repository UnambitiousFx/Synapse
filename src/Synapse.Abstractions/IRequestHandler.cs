using UnambitiousFx.Functional;

namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Defines a contract for a handler responsible for processing a specific type of request.
/// </summary>
/// <typeparam name="TRequest">
///     The type of the request being handled, which must implement
///     <see cref="IRequest{TResponse}" />.
/// </typeparam>
/// <typeparam name="TResponse">The type of the response returned by the handler.</typeparam>
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : notnull
{
    /// <summary>
    ///     Handles the given request asynchronously.
    /// </summary>
    /// <param name="request">The request object containing all required data.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation and contains the result of the request.</returns>
    ValueTask<Result<TResponse>> HandleAsync(TRequest request,
        CancellationToken cancellationToken = default);
}

/// Defines a handler for processing a request.
/// The handler processes the request and optionally returns a response or result.
/// This interface serves as a contract for implementing custom request processing logic.
/// TRequest: The type of the request being handled.
/// Must implement the IRequest interface.
/// TResponse: The type of the response produced by the handler.
/// Must be a notnull type.
public interface IRequestHandler<in TRequest>
    where TRequest : IRequest
{
    /// Handles an incoming request asynchronously and produces a result.
    /// <param name="request">The request to be processed by the handler.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests, with a default value of None.</param>
    /// <return>A task that represents the asynchronous operation, containing a result of the processing.</return>
    ValueTask<Result> HandleAsync(TRequest request,
        CancellationToken cancellationToken = default);
}