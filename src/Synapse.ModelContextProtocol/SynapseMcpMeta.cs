namespace UnambitiousFx.Synapse.ModelContextProtocol;

/// <summary>
///     The <c>_meta</c> keys Synapse writes on a <see cref="global::ModelContextProtocol.Protocol.CallToolResult" />.
/// </summary>
/// <remarks>
///     The MCP specification asks for a prefix on custom <c>_meta</c> keys, so these live under
///     <c>unambitiousfx.synapse/</c>.
/// </remarks>
public static class SynapseMcpMeta
{
    /// <summary>
    ///     Set to <c>true</c> when the answer was cut to <see cref="SynapseMcpOptions.MaxAnswerLength" />.
    /// </summary>
    public const string Truncated = "unambitiousfx.synapse/truncated";

    /// <summary>
    ///     The length of the answer before it was truncated, in UTF-16 code units (<see cref="string.Length" />).
    /// </summary>
    public const string OriginalLength = "unambitiousfx.synapse/originalLength";

    /// <summary>
    ///     The failure code of a tool error, so a client can branch on it without parsing the text.
    /// </summary>
    public const string FailureCode = "unambitiousfx.synapse/failureCode";
}
