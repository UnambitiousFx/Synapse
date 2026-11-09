using UnambitiousFx.Functional;

namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Request pipeline behavior written once for both request shapes: Synapse closes it with
///     <typeparamref name="TResult" /> = <see cref="Result" /> for <see cref="IRequest" /> requests and
///     <see cref="Result{TValue}" /> for <see cref="IRequest{TResponse}" /> requests.
/// </summary>
/// <remarks>
///     Use it for cross-cutting logic that does not depend on the response type (validation, authorization,
///     exception-to-failure mapping, logging). Build a failure with <see cref="RequestNext{TRequest,TResult}.Fail" />
///     and read the outcome through <see cref="IResult" />. Pipeline position is controlled by implementing
///     <see cref="IOrderedPipelineBehavior" />, like the other behaviors.
/// </remarks>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResult"><see cref="Result" /> or <see cref="Result{TValue}" />.</typeparam>
public interface IResultPipelineBehavior<TRequest, TResult>
    where TResult : IResult
{
    /// <summary>
    ///     Handles the request within the pipeline.
    /// </summary>
    /// <param name="request">The request instance.</param>
    /// <param name="next">Invokes the rest of the pipeline, or builds a failure result of the right shape.</param>
    /// <param name="cancellationToken">Token for cancelling the operation.</param>
    /// <returns>The result of the rest of the pipeline, or a failure.</returns>
    ValueTask<TResult> HandleAsync(TRequest request,
        RequestNext<TRequest, TResult> next,
        CancellationToken cancellationToken = default);
}
