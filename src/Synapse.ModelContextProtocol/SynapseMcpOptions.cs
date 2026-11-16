using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol;
using UnambitiousFx.Functional.Failures;

namespace UnambitiousFx.Synapse.ModelContextProtocol;

/// <summary>
///     Options for <see cref="IMcpInvoker" /> and the default <see cref="IMcpFailurePolicy" />.
/// </summary>
public sealed class SynapseMcpOptions
{
    /// <summary>
    ///     The failure code of an answer refused with <see cref="ModelContextProtocol.OversizedAnswer.Refuse" />.
    /// </summary>
    public const string AnswerTooLargeCode = "ANSWER_TOO_LARGE";

    private int? _maxAnswerLength;
    private OversizedAnswer? _oversizedCommandAnswer;
    private OversizedAnswer _oversizedAnswer = OversizedAnswer.Truncate;

    /// <summary>
    ///     Whether a <see cref="NotFoundFailure" /> and an <see cref="UnauthorizedFailure" /> produce the same
    ///     "not found" answer. Defaults to <c>true</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         An assistant calls tools with identifiers it guessed, read somewhere, or was given by someone else.
    ///         If "you may not see this" and "this does not exist" answer differently, the difference tells it which
    ///         items exist. With this on, both give the same code and the same fixed message, so nothing can be
    ///         learned by probing.
    ///     </para>
    ///     <para>
    ///         The original message is dropped too, because a not-found message often names the item it looked
    ///         for. An <see cref="AggregateFailure" /> holding either failure is hidden the same way.
    ///     </para>
    ///     <para>
    ///         Turn it off only when every caller may know which items exist, for example an internal tool used by
    ///         operators.
    ///     </para>
    /// </remarks>
    public bool HideExistence { get; set; } = true;

    /// <summary>
    ///     Whether the tool error of a <see cref="ValidationFailure" /> lists each error with its field and code, one
    ///     per line, and carries them in <c>_meta</c> under <see cref="SynapseMcpMeta.Errors" />. Defaults to
    ///     <c>true</c>.
    /// </summary>
    /// <remarks>
    ///     A validation failure's own message joins the error messages without their fields, so an assistant cannot
    ///     tell which argument to fix. Errors of a <see cref="ValidationFailure" /> inside an
    ///     <see cref="AggregateFailure" /> are listed too. Existence hiding and exception hiding still take
    ///     precedence. Set it to <c>false</c> to keep the one-line <c>CODE: message</c> form.
    /// </remarks>
    public bool ListValidationErrors { get; set; } = true;

    /// <summary>
    ///     The largest answer sent back to the client, in UTF-16 code units (<see cref="string.Length" />).
    ///     <c>null</c>, the default, means no limit.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The length is counted in UTF-16 code units, not Unicode scalar values and not UTF-8 bytes: a character
    ///         outside the Basic Multilingual Plane (most emoji) counts as two, and the encoded payload can be larger
    ///         than this number. The cut never separates the two halves of a surrogate pair, so a truncated answer can
    ///         be one code unit shorter than the limit.
    ///     </para>
    ///     <para>
    ///         With <see cref="OversizedAnswer" /> left to <see cref="ModelContextProtocol.OversizedAnswer.Truncate" />,
    ///         a longer answer is cut to this length and followed by a second text block saying it was truncated and
    ///         how long it was, so the assistant knows it did not see everything and can ask for less (a filter, a
    ///         page). The result's <c>_meta</c> also carries <see cref="SynapseMcpMeta.Truncated" /> and
    ///         <see cref="SynapseMcpMeta.OriginalLength" />, in the same unit. A truncated answer is no longer valid
    ///         JSON, which is why the flag is explicit rather than left for the client to notice.
    ///     </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or negative.</exception>
    public int? MaxAnswerLength
    {
        get => _maxAnswerLength;
        set
        {
            if (value is <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "MaxAnswerLength must be positive.");
            }

            _maxAnswerLength = value;
        }
    }

    /// <summary>
    ///     What happens to an answer longer than <see cref="MaxAnswerLength" />: cut it
    ///     (<see cref="ModelContextProtocol.OversizedAnswer.Truncate" />, the default) or answer a tool error instead
    ///     (<see cref="ModelContextProtocol.OversizedAnswer.Refuse" />).
    /// </summary>
    /// <remarks>
    ///     A refusal's text is <c>ANSWER_TOO_LARGE: The answer is N characters, above the M limit: narrow the request
    ///     (...)</c>, and its <c>_meta</c> carries <see cref="SynapseMcpMeta.FailureCode" />,
    ///     <see cref="SynapseMcpMeta.OriginalLength" /> and <see cref="SynapseMcpMeta.MaxAnswerLength" />. The request
    ///     has already run when its answer is measured, so a refused command's change is kept: set
    ///     <see cref="OversizedCommandAnswer" /> to <see cref="ModelContextProtocol.OversizedAnswer.None" /> to refuse
    ///     query answers only.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     The value is not a defined <see cref="ModelContextProtocol.OversizedAnswer" />.
    /// </exception>
    public OversizedAnswer OversizedAnswer
    {
        get => _oversizedAnswer;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown OversizedAnswer value.");
            }

            _oversizedAnswer = value;
        }
    }

    /// <summary>
    ///     What happens to the answer of a command (<c>ICommand&lt;T&gt;</c>) longer than
    ///     <see cref="MaxAnswerLength" />. <c>null</c>, the default, means the same as <see cref="OversizedAnswer" />.
    /// </summary>
    /// <remarks>
    ///     A command has already run when its answer is measured: refusing it keeps the change while the assistant
    ///     only learns its answer was too large, so it is likely to send the command again. Set this to
    ///     <see cref="ModelContextProtocol.OversizedAnswer.None" /> to send command answers whole while
    ///     <see cref="OversizedAnswer" /> still refuses or cuts query answers.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     The value is not <c>null</c> nor a defined <see cref="ModelContextProtocol.OversizedAnswer" />.
    /// </exception>
    public OversizedAnswer? OversizedCommandAnswer
    {
        get => _oversizedCommandAnswer;
        set
        {
            if (value is { } answer && !Enum.IsDefined(answer))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown OversizedAnswer value.");
            }

            _oversizedCommandAnswer = value;
        }
    }

    /// <summary>
    ///     The source-generated JSON context used to serialize answers. Required under NativeAOT, unless
    ///     <see cref="SerializerOptions" /> is set.
    /// </summary>
    /// <remarks>
    ///     Every response type returned through <see cref="IMcpInvoker" /> must be registered in it
    ///     (<c>[JsonSerializable(typeof(Loan))]</c>); an unregistered type throws
    ///     <see cref="InvalidOperationException" />, which the MCP SDK reports as a tool error. Ignored when
    ///     <see cref="SerializerOptions" /> is set. When both are <c>null</c>, answers are serialized with
    ///     <see cref="McpJsonUtilities.DefaultOptions" />, which falls back to reflection when it is enabled.
    /// </remarks>
    public JsonSerializerContext? JsonContext { get; set; }

    /// <summary>
    ///     The JSON options used to serialize answers, such as the ones the host already uses for HTTP. Takes
    ///     precedence over <see cref="JsonContext" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Used as is: their converters, naming and number handling apply, and their
    ///         <see cref="JsonSerializerOptions.TypeInfoResolver" /> may chain several source-generated contexts
    ///         (<c>JsonTypeInfoResolver.Combine</c>), one per module. Every response type must be resolvable from them;
    ///         an unresolvable type throws <see cref="InvalidOperationException" />. Under NativeAOT, the resolver must
    ///         be source-generated.
    ///     </para>
    ///     <para>
    ///         The options become read-only on the first answer, as with any <see cref="JsonSerializerOptions" />
    ///         used to serialize.
    ///     </para>
    /// </remarks>
    public JsonSerializerOptions? SerializerOptions { get; set; }
}
