using System.Text;
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
///             An <see cref="ExceptionalFailure" />, alone or at any depth inside an <see cref="AggregateFailure" />,
///             gives <see cref="UnexpectedMessage" /> with the outer failure's code: an exception message can hold
///             internals (SQL, paths, stack details) that an assistant must not read back to a user.
///         </item>
///         <item>
///             With <see cref="SynapseMcpOptions.ListValidationErrors" /> on, a <see cref="ValidationFailure" />, alone
///             or inside an <see cref="AggregateFailure" />, gives its code and <see cref="InvalidFieldsMessage" /> (or
///             the aggregate's message), then one line per error with its field and code. The errors are also in
///             <c>_meta</c> under <see cref="SynapseMcpMeta.Errors" />.
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

    /// <summary>
    ///     The first line of a listed <see cref="ValidationFailure" />, whose own message only repeats the errors.
    /// </summary>
    public const string InvalidFieldsMessage = "Some fields are not valid.";

    private readonly bool _hideExistence;
    private readonly bool _listValidationErrors;

    /// <summary>
    ///     Creates the policy from the registered options.
    /// </summary>
    /// <param name="options">The MCP options.</param>
    public DefaultMcpFailurePolicy(IOptions<SynapseMcpOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _hideExistence = options.Value.HideExistence;
        _listValidationErrors = options.Value.ListValidationErrors;
    }

    /// <inheritdoc />
    public CallToolResult ToToolError(Failure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        if (_hideExistence &&
            Contains(failure, static f => f is NotFoundFailure or UnauthorizedFailure))
        {
            return Error(FailureCodes.NotFound, HiddenMessage);
        }

        // Checked at every depth, not only at the top: an aggregate's message is today a fixed text, but nothing in
        // the Functional contract promises it will never be built from the inner messages.
        if (Contains(failure, static f => f is ExceptionalFailure))
        {
            return Error(failure.Code, UnexpectedMessage);
        }

        if (_listValidationErrors && ValidationErrors(failure) is { Count: > 0 } errors)
        {
            return ValidationError(failure, errors);
        }

        return Error(failure.Code, failure.Message);
    }

    private static List<ValidationError> ValidationErrors(Failure failure)
    {
        var errors = new List<ValidationError>();
        Collect(failure, errors);
        return errors;

        static void Collect(Failure failure,
            List<ValidationError> errors)
        {
            if (failure is ValidationFailure validation)
            {
                errors.AddRange(validation.Errors);
            }
            else if (failure is AggregateFailure aggregate)
            {
                foreach (var inner in aggregate.Errors)
                {
                    Collect(inner, errors);
                }
            }
        }
    }

    private static CallToolResult ValidationError(Failure failure,
        List<ValidationError> errors)
    {
        var text = new StringBuilder(failure.Code).Append(": ")
            .Append(failure is ValidationFailure ? InvalidFieldsMessage : failure.Message);
        var meta = new JsonArray();
        foreach (var error in errors)
        {
            text.Append("\n- ");
            if (error.Field is not null)
            {
                text.Append(error.Field).Append(error.Code is null ? "" : " ");
            }

            if (error.Code is not null)
            {
                text.Append('(').Append(error.Code).Append(')');
            }

            text.Append(error.Field is null && error.Code is null ? "" : ": ").Append(error.Message);

            var item = new JsonObject();
            if (error.Field is not null)
            {
                item["field"] = error.Field;
            }

            if (error.Code is not null)
            {
                item["code"] = error.Code;
            }

            item["message"] = error.Message;
            meta.Add((JsonNode)item);
        }

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = text.ToString() }],
            IsError = true,
            Meta = new JsonObject { [SynapseMcpMeta.FailureCode] = failure.Code, [SynapseMcpMeta.Errors] = meta }
        };
    }

    private static bool Contains(Failure failure,
        Func<Failure, bool> predicate)
    {
        if (predicate(failure))
        {
            return true;
        }

        if (failure is AggregateFailure aggregate)
        {
            foreach (var inner in aggregate.Errors)
            {
                if (Contains(inner, predicate))
                {
                    return true;
                }
            }
        }

        return false;
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
