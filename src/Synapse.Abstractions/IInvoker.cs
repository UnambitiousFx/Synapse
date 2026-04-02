using UnambitiousFx.Functional;

namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Represents a sender that dispatches requests to their corresponding handlers.
/// </summary>
public interface IInvoker
{
    /// <summary>
    ///     Sends a request to the appropriate handler and returns the result.
    ///     The response type is inferred by the compiler from the request's <see cref="IRequest{TResponse}" />
    ///     implementation, so no explicit type arguments are required at the call site.
    /// </summary>
    /// <typeparam name="TResponse">
    ///     The type of the response. Inferred from the request argument. Must be a non-nullable type.
    /// </typeparam>
    /// <param name="request">
    ///     The request object to be sent. This parameter cannot be null.
    /// </param>
    /// <param name="cancellationToken">
    ///     A cancellation token that can be used to cancel the operation. Defaults to <see cref="CancellationToken.None" />.
    /// </param>
    /// <returns>
    ///     A task representing the asynchronous operation. The task result contains a <see cref="Result{TValue}" />
    ///     holding the response of type <typeparamref name="TResponse" />.
    /// </returns>
    ValueTask<Result<TResponse>> InvokeAsync<TResponse>(IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
        where TResponse : notnull;

    /// <summary>
    ///     Sends a void request asynchronously and returns a result.
    /// </summary>
    /// <typeparam name="TRequest">The type of the request object. It must implement the <see cref="IRequest" /> interface.</typeparam>
    /// <param name="request">The request object to be processed.</param>
    /// <param name="cancellationToken">An optional cancellation token to cancel the operation.</param>
    /// <returns>A <see cref="ValueTask{Result}" /> that represents the result of the operation.</returns>
    ValueTask<Result> InvokeAsync<TRequest>(TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest;

    /// <summary>
    ///     Sends a streaming request and returns an async enumerable of results.
    ///     The item type is inferred by the compiler from the request's <see cref="IStreamRequest{TResponse}" />
    ///     implementation.
    /// </summary>
    /// <typeparam name="TItem">
    ///     The type of items in the stream. Inferred from the request argument. Must be a non-nullable type.
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
    IAsyncEnumerable<Result<TItem>> InvokeStreamAsync<TItem>(IStreamRequest<TItem> request,
        CancellationToken cancellationToken = default)
        where TItem : notnull;
}