using UnambitiousFx.Functional;

namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Typed request pipeline behavior that only applies to a specific request type (without response).
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
public interface IRequestPipelineBehavior<in TRequest>
    where TRequest : IRequest
{
    /// <summary>
    ///     Handles the request within the pipeline for the specific <typeparamref name="TRequest" /> type.
    /// </summary>
    /// <param name="request">The request instance.</param>
    /// <param name="next">Delegate to invoke the next behavior/handler.</param>
    /// <param name="cancellationToken">Token for cancelling the operation.</param>
    /// <returns>A task containing the result of the operation.</returns>
    ValueTask<Result> HandleAsync(TRequest request,
        RequestHandlerDelegate next,
        CancellationToken cancellationToken = default);
}

/// <summary>
///     Typed request pipeline behavior that only applies to a specific request/response pair.
/// </summary>
/// <typeparam name="TRequest">Request type.</typeparam>
/// <typeparam name="TResponse">Response type.</typeparam>
public interface IRequestPipelineBehavior<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : notnull
{
    /// <summary>
    ///     Handles the request within the pipeline for the specific <typeparamref name="TRequest" /> returning a
    ///     <typeparamref name="TResponse" />.
    /// </summary>
    /// <param name="request">The request instance.</param>
    /// <param name="next">Delegate to invoke the next behavior/handler.</param>
    /// <param name="cancellationToken">Token for cancelling the operation.</param>
    /// <returns>A task containing the result of the operation.</returns>
    ValueTask<Result<TResponse>> HandleAsync(TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default);
}