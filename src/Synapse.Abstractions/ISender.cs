using UnambitiousFx.Functional;

namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Represents a sender that dispatches requests to their corresponding handlers.
/// </summary>
public interface ISender
{
    /// Sends a request to the appropriate handler and returns the result.
    /// <typeparam name="TRequest">
    ///     The type of the request message. Must implement <see cref="IRequest{TResponse}" />.
    /// </typeparam>
    /// <typeparam name="TResponse">
    ///     The type of the response message. The response must be a non-nullable type.
    /// </typeparam>
    /// <param name="request">
    ///     The request object to be sent. This parameter cannot be null.
    /// </param>
    /// <param name="cancellationToken">
    ///     A cancellation token that can be used to cancel the operation. Defaults to <see cref="CancellationToken.None" />.
    /// </param>
    /// <returns>
    ///     A task representing the asynchronous operation. The task result contains a <see cref="Result" />
    ///     holding the response of type <typeparamref name="TResponse" />.
    /// </returns>
    ValueTask<Result<TResponse>> SendAsync<TRequest, TResponse>(TRequest request,
        CancellationToken cancellationToken = default)
        where TResponse : notnull
        where TRequest : IRequest<TResponse>;

    /// Sends a request asynchronously and returns a result.
    /// <typeparam name="TRequest">The type of the request object. It must implement the <see cref="IRequest" /> interface.</typeparam>
    /// <param name="request">The request object to be processed.</param>
    /// <param name="cancellationToken">An optional cancellation token to cancel the operation.</param>
    /// <returns>A <see cref="ValueTask{Result}" /> that represents the result of the operation.</returns>
    ValueTask<Result> SendAsync<TRequest>(TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest;

    /// Sends a streaming request and returns an async enumerable of results.
    /// <typeparam name="TRequest">
    ///     The type of the streaming request message. Must implement <see cref="IStreamRequest{TItem}" />.
    /// </typeparam>
    /// <typeparam name="TItem">
    ///     The type of items in the stream. The item type must be a non-nullable type.
    /// </typeparam>
    /// <param name="request">
    ///     The streaming request object to be sent. This parameter cannot be null.
    /// </param>
    /// <param name="cancellationToken">
    ///     A cancellation token that can be used to cancel the streaming operation.
    ///     Defaults to <see cref="CancellationToken.None" />.
    /// </param>
    /// <returns>
    ///     An asynchronous enumerable sequence of <see cref="Result{TValue}" /> objects,
    ///     where each result holds an item of type <typeparamref name="TItem" /> or an error.
    /// </returns>
    IAsyncEnumerable<Result<TItem>> SendStreamAsync<TRequest, TItem>(TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : IStreamRequest<TItem>
        where TItem : notnull;
}