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
        RequestHandlerDelegate<TReq> next,
        CancellationToken cancellationToken = default)
        where TReq : IRequest
    {
        // This typed behavior only applies to requests with a response.
        return next(request, cancellationToken);
    }

    public ValueTask<Result<TRes>> HandleAsync<TReq, TRes>(TReq request,
        RequestHandlerDelegate<TReq, TRes> next,
        CancellationToken cancellationToken = default)
        where TRes : notnull
        where TReq : IRequest<TRes>
    {
        if (request is TRequest typed &&
            typeof(TRes) == typeof(TResponse))
        {
            // Guard the bridge so typed behaviors cannot call next with an incompatible request instance.
            RequestHandlerDelegate<TRequest, TResponse> adaptedNext = (req, ct) =>
            {
                if (req is not TReq concrete)
                {
                    throw new InvalidOperationException(
                        $"Typed behavior for '{typeof(TRequest).Name}' invoked next with '{req.GetType().Name}', " +
                        $"which is not assignable to '{typeof(TReq).Name}'.");
                }

                return BridgeNext(next, concrete, ct);
            };

            var vt = _inner.HandleAsync(typed, adaptedNext, cancellationToken);
            return (ValueTask<Result<TRes>>)(object)vt;
        }

        return next(request, cancellationToken);

        static ValueTask<Result<TResponse>> BridgeNext(RequestHandlerDelegate<TReq, TRes> nextDelegate,
            TReq concreteRequest,
            CancellationToken ct)
        {
            var nextResult = nextDelegate(concreteRequest, ct);
            return (ValueTask<Result<TResponse>>)(object)nextResult;
        }
    }
}