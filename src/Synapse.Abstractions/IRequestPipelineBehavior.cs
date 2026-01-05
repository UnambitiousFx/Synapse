using UnambitiousFx.Functional;

namespace UnambitiousFx.Synapse.Abstractions;

/// Provides a mechanism to define custom behavior for handling requests in a processing pipeline.
/// Implementers of this interface can encapsulate logic that needs to be executed before or after
/// the request handler is invoked.
/// The interface supports two forms of request handling:
/// 1. A request without a response.
/// 2. A request that yields a specific response.
/// Implementations can utilize the provided `context`, `request`, and the `next` delegate to
/// customize or extend the handling process. The `next` delegate represents the subsequent
/// middleware or the actual request handler in the pipeline.
/// Generic Type Parameters:
/// - TRequest: Represents the type of the request being handled.
/// - TResponse: Represents the type of the response produced by the request.
public interface IRequestPipelineBehavior
{
    /// Handles the processing of a request asynchronously within the context of a pipeline behavior.
    /// <typeparam name="TRequest">The type of the request object being processed.</typeparam>
    /// <param name="request">The request object to be processed.</param>
    /// <param name="next">The delegate to invoke the next step in the processing pipeline.</param>
    /// <param name="cancellationToken">
    ///     A token to observe while waiting for the task to complete, allowing cancellation if
    ///     needed.
    /// </param>
    /// <returns>
    ///     A task representing the asynchronous operation, containing a result object indicating the success or failure
    ///     of the process.
    /// </returns>
    ValueTask<Result> HandleAsync<TRequest>(TRequest request,
        RequestHandlerDelegate next,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest;

    /// Handles the execution of a request pipeline behavior.
    /// <typeparam name="TRequest">The type of the request being handled. Must implement <see cref="IRequest{TResponse}" />.</typeparam>
    /// <typeparam name="TResponse">The type of the response expected from handling the request. Must be a non-nullable value.</typeparam>
    /// <param name="request">The request object to handle within the behavior.</param>
    /// <param name="next">The delegate representing the next step in the request pipeline.</param>
    /// <param name="cancellationToken">The optional token to propagate the notification that the operation should be canceled.</param>
    /// <returns>
    ///     A <see cref="ValueTask{TResult}" /> containing a <see cref="Result{TResponse}" /> which indicates the outcome
    ///     of the request handling process.
    /// </returns>
    ValueTask<Result<TResponse>> HandleAsync<TRequest, TResponse>(TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
        where TResponse : notnull
        where TRequest : IRequest<TResponse>;
}