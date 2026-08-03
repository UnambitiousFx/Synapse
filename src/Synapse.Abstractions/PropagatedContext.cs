using System.Diagnostics;

namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Flow state recovered from an inbound boundary — an HTTP request, a broker message, or any other
///     transport — before a context exists for the current unit of work.
/// </summary>
/// <remarks>
///     <para>
///         This is the hand-off between a boundary adapter and <see cref="IContextFactory" />. The adapter
///         extracts; the factory decides what to adopt. Nothing here is trusted: an adapter that read these
///         values off the wire must have already validated and size-capped them.
///     </para>
///     <para>
///         Identity lives in <see cref="Trace" /> alone. Synapse adds no identifiers of its own to the wire —
///         W3C trace context already carries the trace id and the sender's span id, so a second scheme
///         alongside it would be a synonym.
///     </para>
///     <para>
///         The default value means "nothing was propagated", which is how a flow that starts inside this
///         process is represented.
///     </para>
///     <para>
///         "Nothing was propagated" and "what was propagated was rejected" are different states, which is what
///         <see cref="SuppressAmbientTrace" /> distinguishes. Clearing <see cref="Trace" /> is not enough to
///         refuse a caller's identity: the host's own request instrumentation parses <c>traceparent</c> before
///         any middleware runs, so the ambient <see cref="Activity" /> already carries the caller's trace id and
///         the factory would adopt it from there.
///     </para>
/// </remarks>
/// <param name="Trace">
///     The inbound W3C trace context. Its trace id becomes the flow's <see cref="IContext.TraceId" /> and its
///     span id becomes <see cref="IContext.CausationId" />. Parsed by the platform's
///     <see cref="DistributedContextPropagator" />, never by Synapse itself.
/// </param>
/// <param name="Baggage">
///     The inbound baggage entries, or <c>null</c> when none were present. Business values only.
/// </param>
/// <param name="SuppressAmbientTrace">
///     Whether the ambient <see cref="Activity" /> is disqualified as a source of identity. Set by a boundary
///     adapter that deliberately discarded inbound trace context; see <see cref="IContextFactory" />.
/// </param>
public readonly record struct PropagatedContext(
    ActivityContext Trace,
    IReadOnlyDictionary<string, string>? Baggage,
    bool SuppressAmbientTrace = false)
{
    /// <summary>
    ///     Nothing was propagated — the unit of work starts here.
    /// </summary>
    public static PropagatedContext None => default;

    /// <summary>
    ///     Gets a value indicating whether no flow state was recovered at all.
    /// </summary>
    public bool IsEmpty => Trace == default &&
                           Baggage is null or { Count: 0 };
}
