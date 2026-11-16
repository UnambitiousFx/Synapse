namespace UnambitiousFx.Synapse.ModelContextProtocol;

/// <summary>
///     What <see cref="IMcpInvoker" /> does with an answer longer than <see cref="SynapseMcpOptions.MaxAnswerLength" />.
/// </summary>
public enum OversizedAnswer
{
    /// <summary>
    ///     Cut the answer and add a block saying it was truncated. The cut JSON is not valid, and a model may read the
    ///     first items as the whole answer.
    /// </summary>
    Truncate,

    /// <summary>
    ///     Send a tool error with code <see cref="SynapseMcpOptions.AnswerTooLargeCode" /> asking for a narrower
    ///     request instead of a partial answer. Safer for data an assistant computes with (amounts, totals, counts).
    /// </summary>
    Refuse
}
