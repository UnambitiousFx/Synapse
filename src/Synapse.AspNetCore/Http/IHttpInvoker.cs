using UnambitiousFx.Synapse.Abstractions;
using IHttpResult = Microsoft.AspNetCore.Http.IResult;

namespace UnambitiousFx.Synapse.AspNetCore.Http;

/// <summary>
///     Defines an abstraction for handling HTTP requests with a mediator pattern, enabling the execution of requests
///     and mapping their results to HTTP responses.
/// </summary>
public interface IHttpInvoker
{
    /// <summary>
    ///     Executes an asynchronous operation for a specified request and generates an HTTP result.
    ///     The response type is inferred by the compiler from the request's <see cref="IRequest{TResponse}" />
    ///     implementation, so no explicit type arguments are required at the call site.
    /// </summary>
    /// <typeparam name="TResponse">
    ///     The type of the response. Inferred from the request argument. Must not be
    ///     <see langword="null" />.
    /// </typeparam>
    /// <param name="request">The request object to be processed.</param>
    /// <param name="cancellationToken">An optional <see cref="CancellationToken" /> to observe while awaiting the task.</param>
    /// <returns>A <see cref="ValueTask{TResult}" /> containing an HTTP result representing the outcome of the operation.</returns>
    ValueTask<IHttpResult> InvokeAsync<TResponse>(IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
        where TResponse : notnull;

    /// <summary>
    ///     Executes an asynchronous operation for a specified request and maps the successful response to a custom
    ///     HTTP result via <paramref name="onSuccess" />. Failures are mapped through the registered failure mapper.
    ///     The response type is inferred by the compiler from the request's <see cref="IRequest{TResponse}" />
    ///     implementation.
    /// </summary>
    /// <typeparam name="TResponse">
    ///     The type of the response. Inferred from the request argument. Must not be
    ///     <see langword="null" />.
    /// </typeparam>
    /// <param name="request">The request object to be processed.</param>
    /// <param name="onSuccess">A factory that receives the success value and returns the desired HTTP result.</param>
    /// <param name="cancellationToken">An optional <see cref="CancellationToken" /> to observe while awaiting the task.</param>
    /// <returns>A <see cref="ValueTask{TResult}" /> containing an HTTP result representing the outcome of the operation.</returns>
    ValueTask<IHttpResult> InvokeAsync<TResponse>(IRequest<TResponse> request,
        Func<TResponse, IHttpResult> onSuccess,
        CancellationToken cancellationToken = default)
        where TResponse : notnull;

    /// <summary>
    ///     Invokes the specified void request using the mediator pattern, mapping the result to an HTTP response.
    /// </summary>
    /// <typeparam name="TRequest">The type of the request to be invoked. Must implement <see cref="IRequest" />.</typeparam>
    /// <param name="request">The request instance to be invoked.</param>
    /// <param name="cancellationToken">An optional token to cancel the operation.</param>
    /// <returns>A <see cref="ValueTask{TResult}" /> containing the HTTP result.</returns>
    ValueTask<IHttpResult> InvokeAsync<TRequest>(TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest;

    /// <summary>
    ///     Executes a streaming request and returns an <see cref="IAsyncEnumerable{TItem}" /> of successfully
    ///     unwrapped items. Failed items in the stream are silently skipped.
    ///     The item type is inferred by the compiler from the request's <see cref="IStreamRequest{TResponse}" />
    ///     implementation.
    /// </summary>
    /// <typeparam name="TItem">
    ///     The type of items in the stream. Inferred from the request argument. Must not be
    ///     <see langword="null" />.
    /// </typeparam>
    /// <param name="request">The streaming request.</param>
    /// <param name="cancellationToken">An optional <see cref="CancellationToken" /> to observe while iterating.</param>
    /// <returns>An async sequence of successfully resolved items.</returns>
    IAsyncEnumerable<TItem> InvokeStreamAsync<TItem>(IStreamRequest<TItem> request,
        CancellationToken cancellationToken = default)
        where TItem : notnull;
}