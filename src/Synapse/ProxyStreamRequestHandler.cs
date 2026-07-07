using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Pipelines;

namespace UnambitiousFx.Synapse;

/// <summary>
///     Proxy handler that wraps a streaming request handler with pipeline behaviors.
///     This enables cross-cutting concerns like logging, validation, and caching for streaming requests.
/// </summary>
/// <typeparam name="TRequestHandler">The concrete streaming request handler type.</typeparam>
/// <typeparam name="TRequest">The streaming request type.</typeparam>
/// <typeparam name="TItem">The type of items yielded by the stream.</typeparam>
internal sealed class ProxyStreamRequestHandler<TRequestHandler, TRequest, TItem>(
    TRequestHandler handler,
    IEnumerable<IStreamRequestPipelineBehavior<TRequest, TItem>> behaviors)
    : IStreamRequestHandler<TRequest, TItem>
    where TRequestHandler : class, IStreamRequestHandler<TRequest, TItem>
    where TRequest : IStreamRequest<TItem>
    where TItem : notnull
{
    private readonly ImmutableArray<IStreamRequestPipelineBehavior<TRequest, TItem>> _behaviors =
        [.. behaviors.OrderBy(PipelineBehaviorOrdering.OrderOf)];

    // Unlike the request/event proxies, the streaming chain is NOT composed once in the constructor:
    // StreamRequestHandlerDelegate<TItem> takes no parameters, so each behavior's `next` must capture the
    // per-dispatch request and cancellation token — it cannot be built ahead of a dispatch. The dominant
    // per-dispatch cost here is the async-iterator state machine, not the small `next` closure, so the
    // recursive driver below is kept deliberately. Behaviors are still sorted once, above.

    public async IAsyncEnumerable<Result<TItem>> HandleAsync(TRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in ExecutePipelineAsync(request, 0, cancellationToken))
        {
            yield return item;
        }
    }

    private async IAsyncEnumerable<Result<TItem>> ExecutePipelineAsync(TRequest request,
        int index,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (index >= _behaviors.Length)
        {
            await foreach (var item in handler.HandleAsync(request, cancellationToken))
            {
                yield return item;
            }

            yield break;
        }

        await foreach (var item in _behaviors[index]
                           .HandleAsync(request, Next, cancellationToken))
        {
            yield return item;
        }

        IAsyncEnumerable<Result<TItem>> Next()
        {
            return ExecutePipelineAsync(request, index + 1, cancellationToken);
        }
    }
}
