using System.Collections.Concurrent;

namespace UnambitiousFx.Examples.MinimalApi.Modules.Notifications;

/// <summary>A notification entry recorded when an <c>OrderPlacedEvent</c> is received.</summary>
public sealed record NotificationEntry
{
    public required Guid OrderId { get; init; }
    public required string Product { get; init; }
    public required int Quantity { get; init; }
    public required DateTime ReceivedAt { get; init; }
}

/// <summary>
///     Thread-safe in-memory log of received notifications. Registered as a singleton so state
///     persists across requests. Call <c>GET /notifications</c> to inspect received entries.
/// </summary>
public sealed class NotificationLog
{
    private readonly ConcurrentQueue<NotificationEntry> _entries = new();

    /// <summary>Appends a notification entry to the log.</summary>
    public void Add(NotificationEntry entry) => _entries.Enqueue(entry);

    /// <summary>Returns all recorded notifications in arrival order.</summary>
    public NotificationEntry[] All => _entries.ToArray();
}
