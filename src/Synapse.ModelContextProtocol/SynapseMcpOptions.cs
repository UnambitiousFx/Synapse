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
    ///     The largest answer, in characters, sent back to the client. <c>null</c>, the default, means no limit.
    /// </summary>
    /// <remarks>
    ///     A longer answer is cut to this length and followed by a second text block saying it was truncated and how
    ///     long it was, so the assistant knows it did not see everything and can ask for less (a filter, a page).
    ///     The result's <c>_meta</c> also carries <see cref="SynapseMcpMeta.Truncated" />. A truncated answer is no
    ///     longer valid JSON, which is why the flag is explicit rather than left for the client to notice.
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
    ///     The source-generated JSON context used to serialize answers. Required under NativeAOT.
    /// </summary>
    /// <remarks>
    ///     Every response type returned through <see cref="IMcpInvoker" /> must be registered in it
    ///     (<c>[JsonSerializable(typeof(Loan))]</c>); an unregistered type throws
    ///     <see cref="InvalidOperationException" />, which the MCP SDK reports as a tool error. When <c>null</c>,
    ///     answers are serialized with <see cref="McpJsonUtilities.DefaultOptions" />, which falls back to
    ///     reflection when it is enabled.
    /// </remarks>
    public JsonSerializerContext? JsonContext { get; set; }
}
