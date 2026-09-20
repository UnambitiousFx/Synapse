using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Pipelines;

namespace UnambitiousFx.Synapse;

internal sealed class ProxyRequestHandler<TRequestHandler, TRequest>
    : IRequestHandler<TRequest>, IPipelineInfo
    where TRequestHandler : class, IRequestHandler<TRequest>
    where TRequest : IRequest
{
    private readonly IRequestPipelineBehavior<TRequest>[] _behaviors;
    // The behavior chain is composed once here rather than recursed per dispatch: each dispatch then
    // just invokes the head delegate, with no per-behavior closure allocation on the hot path. The proxy
    // shares its handler's lifetime, so a singleton handler composes the chain exactly once.
    private readonly RequestHandlerDelegate<TRequest> _pipeline;

    public ProxyRequestHandler(TRequestHandler handler,
        IEnumerable<IRequestPipelineBehavior<TRequest>> behaviors)
    {
        // Ordered by runtime pipeline position (IOrderedPipelineBehavior); the stable sort keeps
        // registration order for behaviors that share an Order.
        var sorted = behaviors.OrderBy(PipelineBehaviorOrdering.OrderOf).ToArray();
        _behaviors = sorted;

        RequestHandlerDelegate<TRequest> next = handler.HandleAsync;
        // Wrap in reverse so the lowest-Order behavior ends up outermost (runs first).
        for (var i = sorted.Length - 1; i >= 0; i--)
        {
            var behavior = sorted[i];
            var captured = next;
            next = (req, ct) => behavior.HandleAsync(req, captured, ct);
        }

        _pipeline = next;
    }

    Type IPipelineInfo.HandlerType => typeof(TRequestHandler);

    IReadOnlyList<BehaviorDescription> IPipelineInfo.Behaviors => PipelineBehaviorOrdering.Describe(_behaviors);

    public ValueTask<Result> HandleAsync(TRequest request,
        CancellationToken cancellationToken = default)
    {
        return _pipeline(request, cancellationToken);
    }
}

internal sealed class ProxyRequestHandler<TRequestHandler, TRequest, TResponse>
    : IRequestHandler<TRequest, TResponse>, IPipelineInfo
    where TRequestHandler : class, IRequestHandler<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : notnull
{
    private readonly IRequestPipelineBehavior<TRequest, TResponse>[] _behaviors;
    // See the no-response proxy above: the chain is composed once and invoked per dispatch.
    private readonly RequestHandlerDelegate<TRequest, TResponse> _pipeline;

    public ProxyRequestHandler(TRequestHandler handler,
        IEnumerable<IRequestPipelineBehavior<TRequest, TResponse>> behaviors)
    {
        var sorted = behaviors.OrderBy(PipelineBehaviorOrdering.OrderOf).ToArray();
        _behaviors = sorted;

        RequestHandlerDelegate<TRequest, TResponse> next = handler.HandleAsync;
        // Wrap in reverse so the lowest-Order behavior ends up outermost (runs first).
        for (var i = sorted.Length - 1; i >= 0; i--)
        {
            var behavior = sorted[i];
            var captured = next;
            next = (req, ct) => behavior.HandleAsync(req, captured, ct);
        }

        _pipeline = next;
    }

    Type IPipelineInfo.HandlerType => typeof(TRequestHandler);

    IReadOnlyList<BehaviorDescription> IPipelineInfo.Behaviors => PipelineBehaviorOrdering.Describe(_behaviors);

    public ValueTask<Result<TResponse>> HandleAsync(TRequest request,
        CancellationToken cancellationToken = default)
    {
        return _pipeline(request, cancellationToken);
    }
}
