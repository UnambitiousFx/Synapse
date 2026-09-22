namespace UnambitiousFx.Synapse.Outbox.EntityFrameworkCore;

/// <summary>
///     EF Core row shape for a stored outbox entry. Public so it can be queried directly or mapped
///     inside a caller-owned <see cref="Microsoft.EntityFrameworkCore.DbContext" /> via
///     <see cref="OutboxEntityTypeConfiguration" />; callers that only need the outbox pattern's public
///     contract should still prefer <see cref="Abstractions.IEventOutboxStorage" /> and
///     <see cref="Abstractions.OutboxEntry" /> over querying this type directly.
/// </summary>
public sealed class OutboxEntity
{
    /// <summary>The stable identity of the stored item.</summary>
    public Guid Id { get; set; }

    /// <summary>The stored event's <see cref="Type.AssemblyQualifiedName" />, used to rehydrate it.</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>The event payload, serialized as JSON.</summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>The propagation headers captured at store time, serialized as JSON.</summary>
    public string Headers { get; set; } = string.Empty;

    /// <summary>When the entry was stored.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the entry was marked processed, if it was.</summary>
    public DateTimeOffset? ProcessedAt { get; set; }

    /// <summary>Whether the entry has been successfully dispatched.</summary>
    public bool Processed { get; set; }

    /// <summary>Whether the entry has exhausted its retries and moved to the dead-letter queue.</summary>
    public bool DeadLetter { get; set; }

    /// <summary>Whether the entry was taken back via <see cref="Abstractions.IDiscardableOutboxStorage" />.</summary>
    public bool Discarded { get; set; }

    /// <summary>The number of failed dispatch attempts recorded so far.</summary>
    public int Attempts { get; set; }

    /// <summary>The reason recorded for the most recent failure, if any.</summary>
    public string? LastError { get; set; }

    /// <summary>When the entry becomes eligible for its next attempt, if it is currently backing off.</summary>
    public DateTimeOffset? NextAttemptAt { get; set; }
}
