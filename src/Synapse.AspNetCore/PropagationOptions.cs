using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.AspNetCore;

/// <summary>
///     Options for the HTTP propagation middleware.
/// </summary>
public sealed record PropagationOptions
{
    /// <summary>
    ///     The default value of <see cref="TraceIdHeaderName" />.
    /// </summary>
    /// <remarks>
    ///     No <c>X-</c> prefix: RFC 6648 deprecated that convention in 2012. And not "Correlation-Id" — the value
    ///     is the W3C trace id, exposed as <see cref="IContext.TraceId" />, so naming the header after a different
    ///     concept would make one value answer to two names.
    /// </remarks>
    public const string DefaultTraceIdHeaderName = "Trace-Id";

    /// <summary>
    ///     The response header carrying the flow's trace id as a bare 32-character hex string.
    ///     Defaults to <see cref="DefaultTraceIdHeaderName" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Response only. Inbound identity is read from the W3C <c>traceparent</c> header, whose name is fixed
    ///         by the specification.
    ///     </para>
    ///     <para>
    ///         This header exists for people: it is the one string an operator or a support ticket names a request
    ///         by, and being a bare trace id it can be pasted straight into a tracing backend. W3C defines no
    ///         header for that, because it is an operational convenience rather than a protocol need — the
    ///         standards-track response header is <c>traceresponse</c>, emitted alongside this one (see
    ///         <see cref="EmitTraceResponse" />).
    ///     </para>
    ///     <para>
    ///         Set this to <c>X-Correlation-Id</c> if you have clients that depend on the older name.
    ///     </para>
    /// </remarks>
    public string TraceIdHeaderName { get; set; } = DefaultTraceIdHeaderName;

    /// <summary>
    ///     Whether to also emit the W3C Trace Context Level 2 <c>traceresponse</c> header.
    ///     Defaults to <c>true</c>.
    /// </summary>
    /// <remarks>
    ///     <c>traceresponse</c> carries the full <c>00-&lt;trace-id&gt;-&lt;span-id&gt;-&lt;flags&gt;</c> form, so
    ///     conformant tooling can continue the trace from a response, whereas <see cref="TraceIdHeaderName" />
    ///     carries the bare trace id a human can paste. The two are complementary; both are written when a context
    ///     exists.
    /// </remarks>
    public bool EmitTraceResponse { get; set; } = true;

    /// <summary>
    ///     Whether inbound trace context and baggage are adopted as-is.
    ///     Defaults to <c>true</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Leave this on for service-to-service traffic behind a gateway — continuing the caller's trace is
    ///         what makes a flow traceable across services, and is the reason this middleware exists.
    ///     </para>
    ///     <para>
    ///         Turn it off on an internet-facing application. A <c>traceparent</c> is chosen by the caller and is
    ///         exactly as forgeable as any other client-supplied value, so a hostile client can pick a trace id
    ///         that collides with another flow's and make log correlation misleading. With this off the trace id
    ///         is minted server-side, inbound trace context and baggage are discarded, and the caller's trace id
    ///         is preserved as baggage under <see cref="ClientTraceIdBaggageKey" /> so it stays queryable without
    ///         being trusted.
    ///     </para>
    ///     <para>
    ///         The trace id is a label for correlating logs and spans. It is never an authorization input, in
    ///         either mode.
    ///     </para>
    /// </remarks>
    public bool TrustIncomingHeader { get; set; } = true;

    /// <summary>
    ///     The baggage key under which an untrusted caller's trace id is recorded.
    ///     Defaults to <c>client.trace_id</c>.
    /// </summary>
    public string ClientTraceIdBaggageKey { get; set; } = "client.trace_id";
}
