using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Pipelines;

internal sealed class RequestTypedBehaviorAdapter<TRequest, TResponse> : IRequestPipelineBehavior
    where TRequest : IRequest<TResponse>
    where TResponse : notnull
{
    private readonly IRequestPipelineBehavior<TRequest, TResponse> _inner;

    public RequestTypedBehaviorAdapter(IRequestPipelineBehavior<TRequest, TResponse> inner)
    {
        _inner = inner;
    }

    public ValueTask<Result> HandleAsync<TReq>(TReq request,
        RequestHandlerDelegate next,
        CancellationToken cancellationToken = default)
        where TReq : IRequest
    {
        // This typed behavior only applies to requests with a response.
        return next();
    }

    public ValueTask<Result<TRes>> HandleAsync<TReq, TRes>(TReq request,
        RequestHandlerDelegate<TRes> next,
        CancellationToken cancellationToken = default)
        where TRes : notnull
        where TReq : IRequest<TRes>
    {
        if (request is TRequest typed &&
            typeof(TRes) == typeof(TResponse))
        {
            // Cast delegates/results through object (safe because we checked type match above).
            var castedNext = (RequestHandlerDelegate<TResponse>)(object)next;
            var vt = _inner.HandleAsync(typed, castedNext, cancellationToken);
            return (ValueTask<Result<TRes>>)(object)vt;
        }

        return next();
    }
}