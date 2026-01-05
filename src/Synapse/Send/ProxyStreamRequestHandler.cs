using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Send;

/// <summary>
///     Proxy handler that wraps a streaming request handler with pipeline behaviors.
///     This enables cross-cutting concerns like logging, validation, and caching for streaming requests.
/// </summary>
/// <typeparam name="TRequestHandler">The concrete streaming request handler type.</typeparam>
/// <typeparam name="TRequest">The streaming request type.</typeparam>
/// <typeparam name="TItem">The type of items yielded by the stream.</typeparam>
internal sealed class ProxyStreamRequestHandler<TRequestHandler, TRequest, TItem>(
    TRequestHandler handler,
    IEnumerable<IStreamRequestPipelineBehavior> behaviors)
    : IStreamRequestHandler<TRequest, TItem>
    where TRequestHandler : class, IStreamRequestHandler<TRequest, TItem>
    where TRequest : IStreamRequest<TItem>
    where TItem : notnull
{
    private readonly ImmutableArray<IStreamRequestPipelineBehavior> _behaviors = [.. behaviors];

    public async IAsyncEnumerable<Result<TItem>> HandleAsync(TRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in ExecutePipelineAsync(request, 0, cancellationToken)) yield return item;
    }

    private async IAsyncEnumerable<Result<TItem>> ExecutePipelineAsync(TRequest request,
        int index,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (index >= _behaviors.Length)
        {
            await foreach (var item in handler.HandleAsync(request, cancellationToken)) yield return item;

            yield break;
        }

        await foreach (var item in _behaviors[index]
                           .HandleAsync(request, Next, cancellationToken))
            yield return item;

        IAsyncEnumerable<Result<TItem>> Next()
        {
            return ExecutePipelineAsync(request, index + 1, cancellationToken);
        }
    }
}