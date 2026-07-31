namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Represents a stored outbox event together with its stable, per-item identity and the flow state captured
///     when it was stored.
/// </summary>
/// <remarks>
///     <para>
///         The <see cref="Id" /> uniquely identifies a single stored item even when several pending items hold
///         value-equal events. Lifecycle operations on <see cref="IEventOutboxStorage" /> address items by this id
///         rather than by the event payload, so the caller always targets the specific item it is processing.
///     </para>
///     <para>
///         <see cref="Headers" /> is what makes an outbox entry traceable back to the action that produced it. An
///         entry may be dispatched long after the producing request ended — possibly after a process restart — so
///         the ambient trace context and flow identity no longer exist by then. Capturing them at store time is
///         the only way a downstream service, or this one, can tie the resulting work back to its cause.
///     </para>
/// </remarks>
/// <param name="Id">The stable identity of the stored outbox item.</param>
/// <param name="Event">The event payload held by the stored item.</param>
/// <param name="Headers">
///     Propagation headers captured when the item was stored — the W3C <c>traceparent</c>, carrying the flow's
///     trace id, and <c>baggage</c>, carrying business values. Empty when no context existed at store time.
/// </param>
public sealed record OutboxEntry(
    Guid Id,
    IEvent Event,
    IReadOnlyDictionary<string, string> Headers)
{
    /// <summary>
    ///     Initializes an entry with no captured propagation headers.
    /// </summary>
    /// <param name="id">The stable identity of the stored outbox item.</param>
    /// <param name="event">The event payload held by the stored item.</param>
    public OutboxEntry(Guid id, IEvent @event)
        : this(id, @event, EmptyHeaders)
    {
    }

    private static IReadOnlyDictionary<string, string> EmptyHeaders { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
