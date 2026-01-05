using UnambitiousFx.Functional;

namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Represents an abstract base class for handling requests within the mediator pattern.
/// </summary>
/// <typeparam name="TRequest">
///     The type of the request being handled, which implements <see cref="IRequest{TResponse}" />.
/// </typeparam>
/// <typeparam name="TResponse">
///     The type of the response returned from handling the request.
/// </typeparam>
public abstract class RequestHandler<TRequest, TResponse> : IRequestHandler<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : notnull
{
    /// Handles the given request asynchronously.
    /// <param name="request">
    ///     The request instance to be handled.
    /// </param>
    /// <param name="cancellationToken">
    ///     An optional token to observe while waiting for the task to complete.
    /// </param>
    /// <return>
    ///     A ValueTask representing the result of the asynchronous operation,
    ///     containing the result of type <typeparamref name="TResponse" />.
    /// </return>
    public ValueTask<Result<TResponse>> HandleAsync(TRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = Handle(request);

        return new ValueTask<Result<TResponse>>(result);
    }

    /// <summary>
    ///     Processes the given request and returns the result of the operation.
    /// </summary>
    /// <param name="request">The specific request to be handled.</param>
    /// <returns>The result of the request wrapped in a <see cref="Result{TResponse}" /> object.</returns>
    protected abstract Result<TResponse> Handle(TRequest request);
}

/// The `RequestHandler` class provides an abstract base for building request handling logic
/// within the mediator framework. It serves as a mechanism to process requests and
/// optionally produce a result, encapsulating reusable logic to handle specific request types.
/// TRequest: The type of the request that the handler processes. Must implement the `IRequest` interface.
/// TResponse: The type of the response produced by the handler. Must be a `notnull` type.
public abstract class RequestHandler<TRequest> : IRequestHandler<TRequest>
    where TRequest : IRequest
{
    /// Handles an asynchronous request operation.
    /// <param name="request">
    ///     The request instance to be handled. This parameter contains the data and information required to process the
    ///     request.
    /// </param>
    /// <param name="cancellationToken">
    ///     A token that allows the operation to be canceled. Defaults to a cancellation token with a non-cancelable state if
    ///     not provided.
    /// </param>
    /// <returns>
    ///     A ValueTask of type Result, representing the outcome of processing the specified request. The result indicates
    ///     success or failure.
    /// </returns>
    public ValueTask<Result> HandleAsync(TRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = Handle(request);
        return new ValueTask<Result>(result);
    }

    /// Handles a request and produces a result. This method must be implemented by derived classes
    /// to provide specific handling logic for a given request type.
    /// <param name="request">The request to be handled.</param>
    /// <returns>A result object representing the outcome of the request processing.</returns>
    protected abstract Result Handle(TRequest request);
}