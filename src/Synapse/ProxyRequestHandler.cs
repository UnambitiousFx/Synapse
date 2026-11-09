using UnambitiousFx.Functional;
using UnambitiousFx.Functional.Failures;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Pipelines;

namespace UnambitiousFx.Synapse;

internal sealed class ProxyRequestHandler<TRequestHandler, TRequest>
    : IRequestHandler<TRequest>, IPipelineInfo
    where TRequestHandler : class, IRequestHandler<TRequest>
    where TRequest : IRequest
{
    private static readonly Func<Failure, Result> Fail = Result.Failure;

    private readonly object[] _behaviors;
    // The behavior chain is composed once here rather than recursed per dispatch: each dispatch then
    // just invokes the head delegate, with no per-behavior closure allocation on the hot path. The proxy
    // shares its handler's lifetime, so a singleton handler composes the chain exactly once.
    private readonly RequestHandlerDelegate<TRequest> _pipeline;

    public ProxyRequestHandler(TRequestHandler handler,
        IEnumerable<IRequestPipelineBehavior<TRequest>> behaviors,
        IEnumerable<IResultPipelineBehavior<TRequest, Result>> resultBehaviors)
    {
        // Both behavior kinds share one chain, ordered by runtime pipeline position
        // (IOrderedPipelineBehavior); the stable sort keeps registration order for behaviors that share an
        // Order, typed behaviors before result behaviors.
        // Tagged with their kind so a class implementing both interfaces runs as the kind it was registered as.
        var sorted = behaviors.Select(b => (Behavior: (object)b, IsResult: false))
            .Concat(resultBehaviors.Select(b => (Behavior: (object)b, IsResult: true)))
            .OrderBy(x => PipelineBehaviorOrdering.OrderOf(x.Behavior)).ToArray();
        _behaviors = Array.ConvertAll(sorted, x => x.Behavior);

        RequestHandlerDelegate<TRequest> next = handler.HandleAsync;
        // Wrap in reverse so the lowest-Order behavior ends up outermost (runs first).
        for (var i = sorted.Length - 1; i >= 0; i--)
        {
            var captured = next;
            if (!sorted[i].IsResult)
            {
                var behavior = (IRequestPipelineBehavior<TRequest>)sorted[i].Behavior;
                next = (req, ct) => behavior.HandleAsync(req, captured, ct);
            }
            else
            {
                var resultBehavior = (IResultPipelineBehavior<TRequest, Result>)sorted[i].Behavior;
                var continuation = new RequestNext<TRequest, Result>(captured.Invoke, Fail);
                next = (req, ct) => resultBehavior.HandleAsync(req, continuation, ct);
            }
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
    private static readonly Func<Failure, Result<TResponse>> Fail = Result.Failure<TResponse>;

    private readonly object[] _behaviors;
    // See the no-response proxy above: the chain is composed once and invoked per dispatch.
    private readonly RequestHandlerDelegate<TRequest, TResponse> _pipeline;

    public ProxyRequestHandler(TRequestHandler handler,
        IEnumerable<IRequestPipelineBehavior<TRequest, TResponse>> behaviors,
        IEnumerable<IResultPipelineBehavior<TRequest, Result<TResponse>>> resultBehaviors)
    {
        // Tagged with their kind so a class implementing both interfaces runs as the kind it was registered as.
        var sorted = behaviors.Select(b => (Behavior: (object)b, IsResult: false))
            .Concat(resultBehaviors.Select(b => (Behavior: (object)b, IsResult: true)))
            .OrderBy(x => PipelineBehaviorOrdering.OrderOf(x.Behavior)).ToArray();
        _behaviors = Array.ConvertAll(sorted, x => x.Behavior);

        RequestHandlerDelegate<TRequest, TResponse> next = handler.HandleAsync;
        // Wrap in reverse so the lowest-Order behavior ends up outermost (runs first).
        for (var i = sorted.Length - 1; i >= 0; i--)
        {
            var captured = next;
            if (!sorted[i].IsResult)
            {
                var behavior = (IRequestPipelineBehavior<TRequest, TResponse>)sorted[i].Behavior;
                next = (req, ct) => behavior.HandleAsync(req, captured, ct);
            }
            else
            {
                var resultBehavior = (IResultPipelineBehavior<TRequest, Result<TResponse>>)sorted[i].Behavior;
                var continuation = new RequestNext<TRequest, Result<TResponse>>(captured.Invoke, Fail);
                next = (req, ct) => resultBehavior.HandleAsync(req, continuation, ct);
            }
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
