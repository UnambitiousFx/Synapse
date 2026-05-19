using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Pipelines;

internal sealed class ConditionalUntypedBehaviorWrapper : IRequestPipelineBehavior
{
    private readonly IRequestPipelineBehavior _inner;
    private readonly Func<object, bool> _predicate;

    public ConditionalUntypedBehaviorWrapper(IRequestPipelineBehavior inner,
        Func<object, bool> predicate)
    {
        _inner = inner;
        _predicate = predicate;
    }

    public ValueTask<Result> HandleAsync<TRequest>(TRequest request,
        RequestHandlerDelegate<TRequest> next,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest
    {
        if (_predicate(request))
        {
            return _inner.HandleAsync(request, next, cancellationToken);
        }

        return next(request, cancellationToken);
    }

    public ValueTask<Result<TResponse>> HandleAsync<TRequest, TResponse>(TRequest request,
        RequestHandlerDelegate<TRequest, TResponse> next,
        CancellationToken cancellationToken = default)
        where TResponse : notnull
        where TRequest : IRequest<TResponse>
    {
        if (_predicate(request))
        {
            return _inner.HandleAsync(request, next, cancellationToken);
        }

        return next(request, cancellationToken);
    }
}