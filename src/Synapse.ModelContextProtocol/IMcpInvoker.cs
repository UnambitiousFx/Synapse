using ModelContextProtocol.Protocol;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.ModelContextProtocol;

/// <summary>
///     Runs a Synapse request from an MCP tool and turns its result into a <see cref="CallToolResult" />.
/// </summary>
/// <remarks>
///     <para>
///         The request goes through the normal pipeline, so authorization, validation and every other behavior
///         apply exactly as they do for any other transport.
///     </para>
///     <para>
///         A success becomes a text content block holding the JSON answer, serialized with
///         <see cref="SynapseMcpOptions.JsonContext" /> and capped at <see cref="SynapseMcpOptions.MaxAnswerLength" />.
///         A failure becomes a tool error (<see cref="CallToolResult.IsError" /> set) built by the registered
///         <see cref="IMcpFailurePolicy" />.
///     </para>
/// </remarks>
public interface IMcpInvoker
{
    /// <summary>
    ///     Sends a request that returns a value, such as an <see cref="IQuery{TResponse}" /> or an
    ///     <see cref="ICommand{TResponse}" />, and maps its result to a tool result.
    ///     The response type is inferred from the request, so no type argument is needed at the call site.
    /// </summary>
    /// <typeparam name="TResponse">The type of the response. Inferred from the request argument.</typeparam>
    /// <param name="request">The request to send.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    ///     The JSON answer on success, or a tool error built by <see cref="IMcpFailurePolicy" /> on failure.
    /// </returns>
    ValueTask<CallToolResult> InvokeAsync<TResponse>(IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
        where TResponse : notnull;

    /// <summary>
    ///     Sends a request that returns no value, such as an <see cref="ICommand" />, and maps its result to a
    ///     tool result.
    /// </summary>
    /// <typeparam name="TRequest">The type of the request.</typeparam>
    /// <param name="request">The request to send.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    ///     A short confirmation on success, or a tool error built by <see cref="IMcpFailurePolicy" /> on failure.
    /// </returns>
    ValueTask<CallToolResult> InvokeAsync<TRequest>(TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest;
}
