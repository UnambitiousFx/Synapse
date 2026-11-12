using ModelContextProtocol.Protocol;
using UnambitiousFx.Functional.Failures;

namespace UnambitiousFx.Synapse.ModelContextProtocol;

/// <summary>
///     Turns the failure of a request into the tool error sent back to the MCP client.
/// </summary>
/// <remarks>
///     Register your own implementation before or instead of <c>AddSynapseMcp</c> (it uses <c>TryAdd</c>) to change
///     what an assistant sees. The result should have <see cref="CallToolResult.IsError" /> set: a tool error is
///     something the model reads and can react to, unlike a protocol error.
/// </remarks>
public interface IMcpFailurePolicy
{
    /// <summary>
    ///     Builds the tool error for a failure.
    /// </summary>
    /// <param name="failure">The failure returned by the pipeline.</param>
    /// <returns>The tool result sent to the client.</returns>
    CallToolResult ToToolError(Failure failure);
}
