using System.Diagnostics;

namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     The identity of a single unit of work as it moves through the system.
/// </summary>
/// <remarks>
///     <para>
///         This is W3C trace context, not a parallel identifier scheme. <see cref="TraceId" /> is the
///         32-character hex trace id — byte for byte the value a tracing backend shows — so one search string
///         works in both application logs and Jaeger, Tempo or Application Insights.
///     </para>
/// </remarks>
/// <param name="TraceId">
///     The 32-character lowercase hex trace id of the flow this unit of work belongs to. Never null or empty.
/// </param>
/// <param name="CausationId">
///     The 16-character hex span id of the immediate predecessor, or <c>null</c> at the root of a flow or when
///     no tracing is wired.
/// </param>
/// <param name="OccurredAt">The instant this unit of work entered the system.</param>
public readonly record struct ContextIdentity(
    string TraceId,
    string? CausationId,
    DateTimeOffset OccurredAt)
{
    /// <summary>
    ///     Creates an identity for a unit of work, taking the trace id from the propagated state when present,
    ///     then from the ambient <see cref="Activity" />, and minting one only as a last resort.
    /// </summary>
    /// <param name="inbound">State recovered at an inbound boundary, or <see cref="PropagatedContext.None" />.</param>
    /// <returns>An identity whose <see cref="TraceId" /> is always populated.</returns>
    /// <remarks>
    ///     <para>
    ///         The minted fallback is what lets Synapse correlate in a host that has wired no tracing at all:
    ///         with no registered <see cref="ActivityListener" />, <c>StartActivity</c> returns null and
    ///         <see cref="Activity.Current" /> stays null, so there would otherwise be no id to log at all.
    ///     </para>
    ///     <para>
    ///         When an activity does exist and carries a real trace id, that id is read rather than invented, so
    ///         <see cref="TraceId" /> equals <c>Activity.Current.TraceId.ToHexString()</c>. An activity with a
    ///         default (all-zeros) trace id is treated as no id at all and one is minted instead.
    ///     </para>
    ///     <para>
    ///         <see cref="PropagatedContext.SuppressAmbientTrace" /> overrides that: the ambient activity is
    ///         skipped and an id is minted even when one is available. A boundary that refused the caller's
    ///         trace context sets it, because the host's request instrumentation has already parented the
    ///         ambient activity to that same caller — so reading the ambient id would readmit exactly the value
    ///         the boundary rejected (see known issue 032).
    ///     </para>
    /// </remarks>
    public static ContextIdentity ForUnitOfWork(PropagatedContext inbound)
    {
        var activity = Activity.Current;

        if (inbound.Trace != default)
        {
            // The sender's trace id is this flow's identity and its span id is what caused this unit of work.
            return new ContextIdentity(inbound.Trace.TraceId.ToHexString(),
                ToNullableHex(inbound.Trace.SpanId), DateTimeOffset.UtcNow);
        }

        if (inbound.SuppressAmbientTrace)
        {
            // Nothing about the caller may become this flow's identity, and the ambient activity is the
            // caller's by proxy. Causation is dropped with it: the only candidate span id is the caller's.
            return new ContextIdentity(ActivityTraceId.CreateRandom().ToHexString(), null,
                DateTimeOffset.UtcNow);
        }

        // A default ActivityTraceId hex-formats to 32 zeros, which is non-empty but useless as an identity, so
        // the ambient id is taken only when it is actually set (see known issue 031). Falling back to the
        // ambient activity's parent covers the case where the boundary adapter did not run but a caller's trace
        // context was still adopted by the host's own instrumentation.
        return new ContextIdentity(AmbientOrMintedTraceId(activity),
            ToNullableHex(activity?.ParentSpanId ?? default), DateTimeOffset.UtcNow);
    }

    private static string AmbientOrMintedTraceId(Activity? activity)
    {
        var ambient = activity?.TraceId ?? default;

        return ambient != default
            ? ambient.ToHexString()
            : ActivityTraceId.CreateRandom().ToHexString();
    }

    private static string? ToNullableHex(ActivitySpanId spanId)
    {
        return spanId == default
            ? null
            : spanId.ToHexString();
    }
}
