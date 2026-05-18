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
            // Wrap next to bridge TReq -> TRequest; safe because the runtime object is TReq.
            RequestHandlerDelegate<TRequest> adaptedNext = (req, ct) => next((TReq)(object)req, ct);
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