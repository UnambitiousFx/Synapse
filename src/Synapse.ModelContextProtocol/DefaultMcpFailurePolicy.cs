using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using UnambitiousFx.Functional.Failures;

namespace UnambitiousFx.Synapse.ModelContextProtocol;

/// <summary>
///     The default <see cref="IMcpFailurePolicy" />: a tool error whose text is <c>CODE: message</c> and whose
///     <c>_meta</c> carries the code under <see cref="SynapseMcpMeta.FailureCode" />.
/// </summary>
/// <remarks>
///     <list type="bullet">
///         <item>
///             With <see cref="SynapseMcpOptions.HideExistence" /> on, a <see cref="NotFoundFailure" /> or an
///             <see cref="UnauthorizedFailure" />, alone or inside an <see cref="AggregateFailure" />, gives
///             <see cref="FailureCodes.NotFound" /> with <see cref="HiddenMessage" />.
///         </item>
///         <item>
///             An <see cref="ExceptionalFailure" /> gives <see cref="UnexpectedMessage" />: an exception message can
///             hold internals (SQL, paths, stack details) that an assistant must not read back to a user.
///         </item>
///         <item>Every other failure gives its own code and message.</item>
///     </list>
/// </remarks>
public sealed class DefaultMcpFailurePolicy : IMcpFailurePolicy
{
    /// <summary>
    ///     The message of a hidden failure. Fixed, so a not-found and a forbidden item cannot be told apart.
    /// </summary>
    public const string HiddenMessage = "The requested item was not found.";

    /// <summary>
    ///     The message of an <see cref="ExceptionalFailure" />.
    /// </summary>
    public const string UnexpectedMessage = "An unexpected error occurred.";

    private readonly bool _hideExistence;

    /// <summary>
    ///     Creates the policy from the registered options.
    /// </summary>
    /// <param name="options">The MCP options.</param>
    public DefaultMcpFailurePolicy(IOptions<SynapseMcpOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _hideExistence = options.Value.HideExistence;
    }

    /// <inheritdoc />
    public CallToolResult ToToolError(Failure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        if (_hideExistence &&
            HidesExistence(failure))
        {
            return Error(FailureCodes.NotFound, HiddenMessage);
        }

        if (failure is ExceptionalFailure)
        {
            return Error(failure.Code, UnexpectedMessage);
        }

        return Error(failure.Code, failure.Message);
    }

    private static bool HidesExistence(Failure failure)
    {
        switch (failure)
        {
            case NotFoundFailure:
            case UnauthorizedFailure:
                return true;
            case AggregateFailure aggregate:
                foreach (var inner in aggregate.Errors)
                {
                    if (HidesExistence(inner))
                    {
                        return true;
                    }
                }

                return false;
            default:
                return false;
        }
    }

    private static CallToolResult Error(string code,
        string message)
    {
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = $"{code}: {message}" }],
            IsError = true,
            Meta = new JsonObject { [SynapseMcpMeta.FailureCode] = code }
        };
    }
}
