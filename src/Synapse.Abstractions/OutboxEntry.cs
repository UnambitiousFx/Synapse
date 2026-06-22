namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Represents a stored outbox event together with its stable, per-item identity.
/// </summary>
/// <remarks>
///     The <see cref="Id" /> uniquely identifies a single stored item even when several pending items hold
///     value-equal events. Lifecycle operations on <see cref="IEventOutboxStorage" /> address items by this id
///     rather than by the event payload, so the caller always targets the specific item it is processing.
/// </remarks>
/// <param name="Id">The stable identity of the stored outbox item.</param>
/// <param name="Event">The event payload held by the stored item.</param>
public sealed record OutboxEntry(Guid Id, IEvent Event);
