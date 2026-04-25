using Microsoft.AspNetCore.Mvc;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.AspNetCore.Mvc;

/// <summary>
///     Represents a service interface for processing HTTP requests using a mediator pattern, enabling the execution of
///     request objects
///     and conversion of their responses into HTTP results.
/// </summary>
public interface IMvcInvoker
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
    /// <returns>An <see cref="IActionResult" /> representing the outcome of the operation.</returns>
    ValueTask<IActionResult> InvokeAsync<TResponse>(IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
        where TResponse : notnull;

    /// <summary>
    ///     Executes an asynchronous operation for a specified request and maps the successful response to a custom
    ///     <see cref="IActionResult" /> via <paramref name="onSuccess" />. Failures are mapped through the registered failure
    ///     mapper.
    ///     The response type is inferred by the compiler from the request's <see cref="IRequest{TResponse}" />
    ///     implementation.
    /// </summary>
    /// <typeparam name="TResponse">
    ///     The type of the response. Inferred from the request argument. Must not be
    ///     <see langword="null" />.
    /// </typeparam>
    /// <param name="request">The request object to be processed.</param>
    /// <param name="onSuccess">A factory that receives the success value and returns the desired <see cref="IActionResult" />.</param>
    /// <param name="cancellationToken">An optional <see cref="CancellationToken" /> to observe while awaiting the task.</param>
    /// <returns>A <see cref="ValueTask{IActionResult}" /> representing the outcome of the operation.</returns>
    ValueTask<IActionResult> InvokeAsync<TResponse>(IRequest<TResponse> request,
        Func<TResponse, IActionResult> onSuccess,
        CancellationToken cancellationToken = default)
        where TResponse : notnull;

    /// <summary>
    ///     Invokes the specified void request using the mediator pattern, mapping the result to an HTTP response.
    /// </summary>
    /// <typeparam name="TRequest">The type of the request to be invoked. Must implement <see cref="IRequest" />.</typeparam>
    /// <param name="request">The request instance to be invoked.</param>
    /// <param name="cancellationToken">An optional token to cancel the operation.</param>
    /// <returns>A <see cref="ValueTask{TResult}" /> containing the HTTP result.</returns>
    ValueTask<IActionResult> InvokeAsync<TRequest>(TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest;
}