using UnambitiousFx.Functional;
using UnambitiousFx.Functional.Failures;

namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     The continuation handed to an <see cref="IResultPipelineBehavior{TRequest,TResult}" />: it invokes the rest
///     of the pipeline and builds a failure of the request's result shape.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResult"><see cref="Result" /> or <see cref="Result{TValue}" />.</typeparam>
public readonly struct RequestNext<TRequest, TResult>
    where TResult : IResult
{
    private readonly Func<TRequest, CancellationToken, ValueTask<TResult>> _next;
    private readonly Func<Failure, TResult> _fail;

    /// <summary>
    ///     Creates a continuation. Synapse builds it when composing the pipeline; construct it yourself only to
    ///     unit-test a behavior, e.g. <c>new RequestNext&lt;MyRequest, Result&gt;((r, ct) => ..., Result.Failure)</c>.
    /// </summary>
    /// <param name="next">Invokes the rest of the pipeline.</param>
    /// <param name="fail">Builds a failure result, typically <c>Result.Failure</c> or <c>Result.Failure&lt;T&gt;</c>.</param>
    public RequestNext(Func<TRequest, CancellationToken, ValueTask<TResult>> next, Func<Failure, TResult> fail)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(fail);
        _next = next;
        _fail = fail;
    }

    /// <summary>
    ///     Invokes the rest of the pipeline (the next behaviors, then the handler).
    /// </summary>
    /// <param name="request">The request to pass on.</param>
    /// <param name="cancellationToken">Token for cancelling the operation.</param>
    /// <returns>The result of the rest of the pipeline.</returns>
    public ValueTask<TResult> InvokeAsync(TRequest request, CancellationToken cancellationToken = default)
    {
        return _next(request, cancellationToken);
    }

    /// <summary>
    ///     Builds a failed <typeparamref name="TResult" /> without invoking the rest of the pipeline.
    /// </summary>
    /// <param name="failure">The failure.</param>
    /// <returns>A failed <see cref="Result" /> or <see cref="Result{TValue}" />.</returns>
    public TResult Fail(Failure failure)
    {
        return _fail(failure);
    }
}
