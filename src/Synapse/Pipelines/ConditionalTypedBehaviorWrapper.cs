using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Pipelines;

internal sealed class ConditionalTypedBehaviorWrapper : IRequestPipelineBehavior
{
    private readonly IRequestPipelineBehavior _innerAdapter;
    private readonly Func<object, bool> _predicate;

    public ConditionalTypedBehaviorWrapper(IRequestPipelineBehavior innerAdapter,
        Func<object, bool> predicate)
    {
        _innerAdapter = innerAdapter;
        _predicate = predicate;
    }

    public ValueTask<Result> HandleAsync<TReq>(TReq request,
        RequestHandlerDelegate next,
        CancellationToken cancellationToken = default)
        where TReq : IRequest
    {
        if (_predicate(request)) return _innerAdapter.HandleAsync(request, next, cancellationToken);

        return next();
    }

    public ValueTask<Result<TResponse>> HandleAsync<TReq, TResponse>(TReq request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
        where TResponse : notnull
        where TReq : IRequest<TResponse>
    {
        if (_predicate(request)) return _innerAdapter.HandleAsync(request, next, cancellationToken);

        return next();
    }
}