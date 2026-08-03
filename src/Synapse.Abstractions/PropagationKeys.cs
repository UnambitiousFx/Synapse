namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     The wire keys Synapse uses to propagate flow state.
/// </summary>
/// <remarks>
///     All three are W3C standard headers. Synapse defines no keys of its own: identity travels in
///     <see cref="TraceParent" />, and <see cref="Baggage" /> carries business values only.
/// </remarks>
public static class PropagationKeys
{
    /// <summary>
    ///     The W3C trace context header, carrying the trace id and the sender's span id. Written and parsed by
    ///     the platform's <c>DistributedContextPropagator</c>, never by Synapse itself.
    /// </summary>
    public const string TraceParent = "traceparent";

    /// <summary>
    ///     The W3C trace state header, carrying vendor-specific trace data.
    /// </summary>
    public const string TraceState = "tracestate";

    /// <summary>
    ///     The W3C Trace Context Level 2 response header, carrying the trace context of the response in the same
    ///     <c>00-&lt;trace-id&gt;-&lt;span-id&gt;-&lt;flags&gt;</c> format as <see cref="TraceParent" />.
    /// </summary>
    public const string TraceResponse = "traceresponse";

    /// <summary>
    ///     The W3C baggage header, carrying business values such as <c>tenant.id</c>.
    /// </summary>
    public const string Baggage = "baggage";

    /// <summary>
    ///     The pre-W3C baggage header used by older ASP.NET Core services, read as a fallback when
    ///     <see cref="Baggage" /> is absent.
    /// </summary>
    public const string LegacyBaggage = "Correlation-Context";
}
