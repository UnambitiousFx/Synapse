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
    private int? _maxAnswerLength;

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
    ///         A longer answer is cut to this length and followed by a second text block saying it was truncated and
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
