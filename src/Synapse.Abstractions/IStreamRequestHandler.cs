using UnambitiousFx.Functional;

namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Defines a handler for streaming requests that yield multiple items asynchronously.
/// </summary>
/// <typeparam name="TRequest">
///     The type of the streaming request being handled, which must implement
///     <see cref="IStreamRequest{TItem}" />.
/// </typeparam>
/// <typeparam name="TItem">The type of items yielded by the stream. Must be a non-nullable type.</typeparam>
public interface IStreamRequestHandler<in TRequest, TItem>
    where TRequest : IStreamRequest<TItem>
    where TItem : notnull
{
    /// <summary>
    ///     Handles the streaming request and yields items asynchronously.
    ///     Each item is wrapped in a Result to allow per-item error handling.
    /// </summary>
    /// <param name="request">The streaming request object containing all required data.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>
    ///     An asynchronous enumerable sequence of results, where each result contains an item of type
    ///     <typeparamref name="TItem" /> or an error.
    /// </returns>
    IAsyncEnumerable<Result<TItem>> HandleAsync(TRequest request,
        CancellationToken cancellationToken = default);
}