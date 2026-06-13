using UnambitiousFx.Functional;

namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Typed streaming request pipeline behavior that only applies to a specific request/item pair.
///     Use this for behaviors that need compile-time type safety for a specific streaming request.
/// </summary>
/// <typeparam name="TRequest">The streaming request type.</typeparam>
/// <typeparam name="TItem">The type of items yielded by the stream.</typeparam>
public interface IStreamRequestPipelineBehavior<in TRequest, TItem>
    where TRequest : IStreamRequest<TItem>
    where TItem : notnull
{
    /// <summary>
    ///     Handles the streaming request within the pipeline for the specific <typeparamref name="TRequest" /> type.
    /// </summary>
    /// <param name="request">The streaming request instance.</param>
    /// <param name="next">Delegate to invoke the next behavior/handler in the pipeline.</param>
    /// <param name="cancellationToken">Token for cancelling the operation.</param>
    /// <returns>An asynchronous enumerable sequence of results.</returns>
    IAsyncEnumerable<Result<TItem>> HandleAsync(TRequest request,
        StreamRequestHandlerDelegate<TItem> next,
        CancellationToken cancellationToken = default);
}
