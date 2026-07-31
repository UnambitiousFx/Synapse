using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Propagation;

/// <summary>
///     Default <see cref="IContextPropagator" />: W3C trace context for the tracing layer, the W3C
///     <c>baggage</c> header for flow identity and business values.
/// </summary>
/// <remarks>
///     <para>
///         Trace context is delegated to <see cref="DistributedContextPropagator.Current" />. Flow identity
///         rides baggage instead of the trace headers because trace context cannot carry it: <c>traceparent</c>
///         is sampling-dependent and per-hop, spans end before an outbox message is dispatched, and retries
///         produce fresh span ids. Baggage is unconditional and durable.
///     </para>
///     <para>
///         The division of labour is strict: the platform writes the trace headers, Synapse writes the baggage
///         header. The platform's own baggage output is discarded, so <c>Activity.Baggage</c> never reaches the
///         wire and <see cref="IContext.Baggage" /> is the only thing that does.
///     </para>
/// </remarks>
internal sealed class W3CContextPropagator : IContextPropagator
{
    private const int TraceIdHexLength = 32;
    private const int SpanIdHexLength = 16;

    private static readonly DistributedContextPropagator.PropagatorGetterCallback Getter =
        static (object? carrier, string key, out string? value, out IEnumerable<string>? values) =>
        {
            values = null;
            value = carrier is IPropagationCarrier propagationCarrier &&
                    propagationCarrier.TryGetValue(key, out var single)
                ? single
                : null;
        };

    /// <summary>
    ///     Forwards the platform propagator's writes except its baggage header.
    /// </summary>
    /// <remarks>
    ///     The platform propagator serializes <c>Activity.Baggage</c> alongside the trace headers — as
    ///     <c>Correlation-Context</c> with the default propagator, as <c>baggage</c> with the W3C one. Synapse owns
    ///     the baggage header: it writes the context's entries and nothing else. Letting the platform's copy
    ///     through would forward inbound baggage that an untrusted boundary deliberately dropped, and would put a
    ///     value that lives only on the activity onto the wire under a second, divergent header (see known issue
    ///     037).
    /// </remarks>
    private static readonly DistributedContextPropagator.PropagatorSetterCallback TraceOnlySetter =
        static (carrier, key, value) =>
        {
            if (string.Equals(key, PropagationKeys.Baggage, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(key, PropagationKeys.LegacyBaggage, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (carrier is IPropagationCarrier propagationCarrier)
            {
                propagationCarrier.Set(key, value);
            }
        };

    private readonly ILogger _logger;

    public W3CContextPropagator(ILogger<W3CContextPropagator>? logger = null)
    {
        _logger = logger ?? NullLogger<W3CContextPropagator>.Instance;
    }

    public void Inject(IContext context,
        IPropagationCarrier carrier)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(carrier);

        // Trace context only. The platform's own baggage header is filtered out — see TraceOnlySetter — so the
        // context is the single source of what leaves this process as baggage.
        if (Activity.Current is { IdFormat: ActivityIdFormat.W3C } activity &&
            activity.TraceId != default)
        {
            if (string.Equals(activity.TraceId.ToHexString(), context.TraceId, StringComparison.Ordinal))
            {
                DistributedContextPropagator.Current.Inject(activity, carrier, TraceOnlySetter);
            }
            else if (RebasedTraceParent(context.TraceId, activity) is { } rebased)
            {
                carrier.Set(PropagationKeys.TraceParent, rebased);
            }
        }
        else if (SyntheticTraceParent(context.TraceId) is { } traceParent)
        {
            carrier.Set(PropagationKeys.TraceParent, traceParent);
        }

        // Business values only. Identity rides traceparent, written above by the platform. A context with no
        // baggage writes no baggage header: there is nothing to say, and saying it with an empty header would
        // make an intermediary's "baggage present" check lie.
        var header = BaggageCodec.Format(context.Baggage);
        if (header is not null)
        {
            carrier.Set(PropagationKeys.Baggage, header);
        }
    }

    public PropagatedContext Extract(IPropagationCarrier carrier)
    {
        ArgumentNullException.ThrowIfNull(carrier);

        DistributedContextPropagator.Current.ExtractTraceIdAndState(carrier, Getter,
            out var traceParent, out var traceState);

        var trace = ActivityContext.TryParse(traceParent, traceState, out var parsedTrace)
            ? parsedTrace
            : default;

        // Fall back to the pre-W3C header name so business values from an older ASP.NET Core service are not
        // silently lost. The W3C name wins when both are present; it is the one a current peer would set
        // deliberately.
        if (!carrier.TryGetValue(PropagationKeys.Baggage, out var baggageHeader) ||
            string.IsNullOrWhiteSpace(baggageHeader))
        {
            carrier.TryGetValue(PropagationKeys.LegacyBaggage, out baggageHeader);
        }

        var entries = BaggageCodec.Parse(baggageHeader, out var dropped);

        if (dropped > 0)
        {
            _logger.LogWarning(
                "Dropped {DroppedCount} inbound baggage entries that were malformed or exceeded the W3C baggage limits ({MaxEntryCount} entries, {MaxTotalBytes} bytes).",
                dropped, BaggageLimits.MaxEntryCount, BaggageLimits.MaxTotalBytes);
        }

        return new PropagatedContext(trace, entries.Count > 0 ? entries : null);
    }

    /// <summary>
    ///     Formats a <c>traceparent</c> that names the context's trace and the ambient activity's span, for the
    ///     case where the two disagree about which trace this is.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         They disagree when a boundary refused the caller's trace context. The host's own request
    ///         instrumentation parses <c>traceparent</c> before any middleware runs, so the ambient activity is
    ///         already parented to the caller, while the context was deliberately given a server-minted trace id
    ///         (see <see cref="PropagatedContext.SuppressAmbientTrace" />). Letting the platform propagator write
    ///         the activity's id would put the caller-chosen trace id back on the wire as this flow's identity —
    ///         forwarding onward exactly the value the boundary rejected, and re-parenting outbox work into a
    ///         trace a client picked (see known issue 032).
    ///     </para>
    ///     <para>
    ///         The context wins, because it is what this service logs and what it reports in its own response
    ///         headers. The span id and the sampled flag still come from the activity: they describe the span
    ///         actually making this call, and only the trace it is filed under is being corrected.
    ///     </para>
    ///     <para>
    ///         <c>tracestate</c> is dropped rather than forwarded. It is vendor state scoped to the caller's
    ///         trace, and this header names a different one.
    ///     </para>
    /// </remarks>
    /// <returns>The header value, or <c>null</c> when the context's trace id cannot form a valid one.</returns>
    private static string? RebasedTraceParent(string traceId,
        Activity activity)
    {
        if (!IsUsableTraceId(traceId))
        {
            return null;
        }

        var flags = (activity.ActivityTraceFlags & ActivityTraceFlags.Recorded) != 0
            ? "01"
            : "00";
        var spanId = activity.SpanId != default
            ? activity.SpanId.ToHexString()
            : SyntheticSpanId(traceId);

        return $"00-{traceId}-{spanId}-{flags}";
    }

    /// <summary>
    ///     Formats a <c>traceparent</c> for a flow that has no span to describe.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Without this, a host that wired no tracing propagated no trace context at all: <c>Activity.Current</c>
    ///         is null, so nothing was written, and the receiver — or the outbox dispatch — started a brand new trace.
    ///         The stored headers of an outbox entry were then unable to tie the resulting work back to its cause,
    ///         which is the one thing they exist for (see known issue 040).
    ///     </para>
    ///     <para>
    ///         The trace id is the context's. The span id is derived from it rather than invented per call, so every
    ///         hop of one flow names the same synthetic span instead of a fresh one each time. The sampled flag is
    ///         <c>00</c>: this span was never recorded, which is exactly what a non-recording peer reports, and is
    ///         why a receiver treats the parent as unavailable rather than looking for it.
    ///     </para>
    /// </remarks>
    /// <returns>The header value, or <c>null</c> when the trace id cannot form a valid one.</returns>
    private static string? SyntheticTraceParent(string traceId)
    {
        if (!IsUsableTraceId(traceId))
        {
            return null;
        }

        return $"00-{traceId}-{SyntheticSpanId(traceId)}-00";
    }

    /// <summary>
    ///     Whether a trace id can be written into a <c>traceparent</c> a conformant peer will accept.
    /// </summary>
    /// <remarks>
    ///     <see cref="IContext.TraceId" /> is contractually 32 lowercase hex characters, but a custom
    ///     <see cref="IContextFactory" /> can return anything, and an unparseable <c>traceparent</c> is worse
    ///     than an absent one: the receiver rejects the whole header and starts a fresh trace, so the flow is
    ///     lost either way and the malformed value is on the wire as well. All-zeros is refused for the same
    ///     reason the spec does — it is the "no trace id" sentinel.
    /// </remarks>
    private static bool IsUsableTraceId(string traceId)
    {
        if (traceId.Length != TraceIdHexLength ||
            traceId.IndexOfAnyExcept('0') < 0)
        {
            return false;
        }

        foreach (var c in traceId)
        {
            if (c is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
            {
                return false;
            }
        }

        return true;
    }

    private static string SyntheticSpanId(string traceId)
    {
        // A span id is half a trace id, so the first half serves. The fallback covers the astronomically unlikely
        // trace id whose leading 64 bits are all zero, which would format as an invalid all-zeros parent id.
        var leading = traceId.AsSpan(0, SpanIdHexLength);

        return leading.IndexOfAnyExcept('0') >= 0
            ? leading.ToString()
            : traceId.Substring(SpanIdHexLength, SpanIdHexLength);
    }
}
