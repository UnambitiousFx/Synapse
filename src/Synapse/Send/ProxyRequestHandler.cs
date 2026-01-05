using System.Collections.Immutable;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Send;

internal sealed class ProxyRequestHandler<TRequestHandler, TRequest>(
    TRequestHandler handler,
    IEnumerable<IRequestPipelineBehavior> behaviors)
    : IRequestHandler<TRequest>
    where TRequestHandler : class, IRequestHandler<TRequest>
    where TRequest : IRequest
{
    private readonly ImmutableArray<IRequestPipelineBehavior> _behaviors = [.. behaviors];

    public ValueTask<Result> HandleAsync(TRequest request,
        CancellationToken cancellationToken = default)
    {
        return ExecutePipelineAsync(request, 0, cancellationToken);
    }

    private ValueTask<Result> ExecutePipelineAsync(TRequest request,
        int index,
        CancellationToken cancellationToken)
    {
        if (index >= _behaviors.Length) return handler.HandleAsync(request, cancellationToken);

        return _behaviors[index]
            .HandleAsync(request, Next, cancellationToken);

        ValueTask<Result> Next()
        {
            return ExecutePipelineAsync(request, index + 1, cancellationToken);
        }
    }
}

internal sealed class ProxyRequestHandler<TRequestHandler, TRequest, TResponse>(
    TRequestHandler handler,
    IEnumerable<IRequestPipelineBehavior> behaviors)
    : IRequestHandler<TRequest, TResponse>
    where TRequestHandler : class, IRequestHandler<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : notnull
{
    private readonly ImmutableArray<IRequestPipelineBehavior> _behaviors = [.. behaviors];

    public ValueTask<Result<TResponse>> HandleAsync(TRequest request,
        CancellationToken cancellationToken = default)
    {
        return ExecutePipelineAsync(request, 0, cancellationToken);
    }

    private ValueTask<Result<TResponse>> ExecutePipelineAsync(TRequest request,
        int index,
        CancellationToken cancellationToken)
    {
        if (index >= _behaviors.Length) return handler.HandleAsync(request, cancellationToken);

        return _behaviors[index]
            .HandleAsync(request, Next, cancellationToken);

        ValueTask<Result<TResponse>> Next()
        {
            return ExecutePipelineAsync(request, index + 1, cancellationToken);
        }
    }
}