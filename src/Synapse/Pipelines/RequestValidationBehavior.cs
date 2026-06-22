using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Pipelines;

/// <summary>
/// Represents a request pipeline behavior that validates an incoming request before invoking the next handler in the pipeline.
/// If validation fails, it prevents further processing and returns a failure result containing the validation errors.
/// </summary>
/// <typeparam name="TRequest">The type of the request being validated.</typeparam>
public class RequestValidationBehavior<TRequest> : IRequestPipelineBehavior<TRequest>, IOrderedPipelineBehavior
    where TRequest : IRequest
{
    /// <summary>
    ///     Runs innermost (closest to the handler) so validation happens after all other behaviors.
    /// </summary>
    public uint Order => IOrderedPipelineBehavior.Last;

    /// <summary>
    /// Represents a collection of validators responsible for validating a specific type of request.
    /// </summary>
    /// <remarks>
    /// This enumerable contains implementations of the <see cref="IRequestValidator{TRequest}"/> interface,
    /// which define custom logic for validating instances of the specified request type.
    /// These validators are executed as part of the request processing pipeline to ensure
    /// that the request satisfies certain preconditions before further processing.
    /// </remarks>
    private readonly IEnumerable<IRequestValidator<TRequest>> _validators;

    /// <summary>
    /// Represents a behavior in the request processing pipeline that performs validation on a request before invoking
    /// the next handler in the pipeline.
    /// </summary>
    /// <param name="validators">The validators executed against the request before the next handler runs.</param>
    public RequestValidationBehavior(IEnumerable<IRequestValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    /// <summary>
    /// Handles the execution of a request, including validation and forwarding to the next handler in the pipeline.
    /// </summary>
    /// <param name="request">
    /// The request instance being processed by the pipeline behavior.
    /// </param>
    /// <param name="next">
    /// A delegate representing the next step in the pipeline to invoke after this behavior.
    /// </param>
    /// <param name="cancellationToken">
    /// A token that propagates notification that the operation should be canceled.
    /// </param>
    /// <returns>
    /// A <see cref="ValueTask"/> representing the result of the request processing, including any validation errors or
    /// successful completion of the pipeline.
    /// </returns>
    public async ValueTask<Result> HandleAsync(TRequest request, RequestHandlerDelegate<TRequest> next, CancellationToken cancellationToken = default)
    {
        var result = await _validators.Select(x => x.ValidateAsync(request, cancellationToken))
            .Combine();

        return await result.Match(() => next(request, cancellationToken),
            error =>
            {
                var r = Result.Failure(error);
                return ValueTask.FromResult(r);
            });
    }
}

/// <summary>
///     Implements a request pipeline behavior that validates an incoming request before delegating
///     the request to the next handler in the pipeline. If validation fails, it returns a result
///     containing the validation errors and avoids further processing.
/// </summary>
/// <typeparam name="TRequest">The type of the request being validated.</typeparam>
/// <typeparam name="TResponse">The type of the response expected from the pipeline handling.</typeparam>
public class RequestValidationBehavior<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>,
    IOrderedPipelineBehavior
    where TRequest : IRequest<TResponse>
    where TResponse : notnull
{
    private readonly IEnumerable<IRequestValidator<TRequest>> _validators;

    /// <summary>
    ///     Runs innermost (closest to the handler) so validation happens after all other behaviors.
    /// </summary>
    public uint Order => IOrderedPipelineBehavior.Last;

    /// <summary>
    ///     Represents a pipeline behavior that validates requests before they are processed by the appropriate request
    ///     handler.
    /// </summary>
    public RequestValidationBehavior(IEnumerable<IRequestValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    /// <summary>
    ///     Handles the current request by validating it using the configured validators.
    ///     If validation succeeds, the request is passed to the next behavior in the pipeline.
    ///     If validation fails, a failed result is returned.
    /// </summary>
    /// <param name="request">The request object to be processed.</param>
    /// <param name="next">The delegate that represents the next behavior in the pipeline.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>
    ///     A <see cref="ValueTask{TResult}" /> of type <see cref="Result" />.
    ///     If validation is successful, the result of the next behavior is returned.
    ///     If validation fails, a failed result with validation errors is returned.
    /// </returns>
    public async ValueTask<Result<TResponse>> HandleAsync(TRequest request,
        RequestHandlerDelegate<TRequest, TResponse> next,
        CancellationToken cancellationToken)
    {
        var result = await _validators.Select(x => x.ValidateAsync(request, cancellationToken))
            .Combine();

        return await result.Match(() => next(request, cancellationToken),
            error =>
            {
                var r = Result.Failure<TResponse>(error);
                return ValueTask.FromResult(r);
            });
    }
}