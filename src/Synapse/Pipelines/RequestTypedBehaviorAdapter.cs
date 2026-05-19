using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Pipelines;

internal sealed class RequestTypedBehaviorAdapter<TRequest> : IRequestPipelineBehavior
    where TRequest : IRequest
{
    private readonly IRequestPipelineBehavior<TRequest> _inner;

    public RequestTypedBehaviorAdapter(IRequestPipelineBehavior<TRequest> inner)
    {
        _inner = inner;
    }

    public ValueTask<Result> HandleAsync<TReq>(TReq request,
        RequestHandlerDelegate<TReq> next,
        CancellationToken cancellationToken = default)
        where TReq : IRequest
    {
        if (request is TRequest typed)
        {
            // Guard the bridge so typed behaviors cannot call next with an incompatible request instance.
            RequestHandlerDelegate<TRequest> adaptedNext = (req, ct) =>
            {
                if (req is not TReq concrete)
                {
                    throw new InvalidOperationException(
                        $"Typed behavior for '{typeof(TRequest).Name}' invoked next with '{req.GetType().Name}', " +
                        $"which is not assignable to '{typeof(TReq).Name}'.");
                }

                return next(concrete, ct);
            };

            return _inner.HandleAsync(typed, adaptedNext, cancellationToken);
        }

        return next(request, cancellationToken);
    }

    public ValueTask<Result<TResponse>> HandleAsync<TReq, TResponse>(TReq request,
        RequestHandlerDelegate<TReq, TResponse> next,
        CancellationToken cancellationToken = default)
        where TResponse : notnull
        where TReq : IRequest<TResponse>
    {
        // This typed behavior only applies to requests without response.
        return next(request, cancellationToken);
    }
}