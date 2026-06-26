using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.MinimalApi.Counter;

/// <summary>
///     Open-generic, response-bearing pipeline behavior registered via <c>[PipelineBehavior]</c>. The source
///     generator cross-products it with this assembly's request handlers and emits CLOSED registrations — here,
///     closed over the value type <c>int</c>. This is the "value-type open-generic pipeline" case from
///     known-issue 001: a closed registration is Native-AOT safe, whereas a runtime open-generic descriptor
///     closed over <c>int</c> would throw at resolution time.
/// </summary>
[PipelineBehavior]
public sealed class CounterTracingBehavior<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>,
    IOrderedPipelineBehavior
    where TRequest : IRequest<TResponse>
    where TResponse : notnull
{
    /// <summary>Runtime pipeline position — sits between outermost and innermost behaviors.</summary>
    public uint Order => 15;

    public ValueTask<Result<TResponse>> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TRequest, TResponse> next,
        CancellationToken cancellationToken = default)
        => next(request, cancellationToken);
}
