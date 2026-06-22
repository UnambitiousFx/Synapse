using System.Collections.Concurrent;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Contexts;

namespace UnambitiousFx.Synapse.Publish.Outbox;

/// <summary>
///     Provides an in-memory implementation of the <see cref="IEventOutboxStorage" /> interface.
///     This class is designed to store and manage events transiently within the application process.
/// </summary>
/// <remarks>
///     <para>
///         This implementation is useful for development and testing scenarios where a persistent storage mechanism is not
///         required.
///         Since the storage is in-memory, all data will be lost when the application process is terminated.
///     </para>
///     <para>
///         This implementation uses <see cref="CorrelationContext.CurrentCorrelationId" /> to partition events by scope,
///         allowing it to be registered as a Singleton while preserving insertion context.
///         Retrieval and lifecycle operations are global across scopes so outbox processing can run from any context.
///     </para>
/// </remarks>
/// <threadsafety>
///     This class is thread-safe and can be safely accessed from multiple threads concurrently.
/// </threadsafety>
public sealed class InMemoryEventOutboxStorage : IEventOutboxStorage
{
    private readonly ConcurrentDictionary<Guid, ConcurrentBag<Item>> _scopedItems = new();

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<OutboxEntry>> GetPendingEventsAsync(CancellationToken cancellationToken = default)
    {
        // Returns items ready for dispatch (not processed, not dead-letter, and past scheduled time)
        var now = DateTimeOffset.UtcNow;
        IReadOnlyList<OutboxEntry> pending = _scopedItems.Values
            .SelectMany(items => items)
            .Where(item =>
                item is { Processed: false, DeadLetter: false } &&
                (item.NextAttemptAt is null || item.NextAttemptAt <= now))
            .Select(item => new OutboxEntry(item.Id, item.Event))
            .ToList();
        return new ValueTask<IReadOnlyList<OutboxEntry>>(pending);
    }

    /// <inheritdoc />
    public ValueTask<Result> MarkAsProcessedAsync(Guid id,
        CancellationToken cancellationToken = default)
    {
        if (!TryFindItem(id, out var item))
        {
            return new ValueTask<Result>(Result.Failure($"Outbox item '{id}' was not found in the outbox storage"));
        }

        item.Processed = true;
        item.ProcessedAt = DateTimeOffset.UtcNow;
        item.LastError = null;
        item.NextAttemptAt = null;
        return new ValueTask<Result>(Result.Success());
    }

    /// <inheritdoc />
    public ValueTask<Result> ClearAsync(CancellationToken cancellationToken = default)
    {
        _scopedItems.Clear();
        return new ValueTask<Result>(Result.Success());
    }

    /// <inheritdoc />
    public ValueTask<Result> AddAsync<TEvent>(TEvent @event,
        CancellationToken cancellationToken = default)
        where TEvent : class, IEvent
    {
        var correlationId = CorrelationContext.CurrentCorrelationId;
        var items = _scopedItems.GetOrAdd(correlationId, _ => new ConcurrentBag<Item>());
        items.Add(new Item(@event));

        return new ValueTask<Result>(Result.Success());
    }

    /// <inheritdoc />
    public ValueTask<Result> MarkAsFailedAsync(Guid id,
        string reason,
        bool deadLetter,
        DateTimeOffset? nextAttemptAt = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryFindItem(id, out var item))
        {
            return new ValueTask<Result>(Result.Failure($"Outbox item '{id}' was not found in the outbox storage"));
        }

        item.Attempts++;
        item.LastError = reason;
        if (deadLetter)
        {
            item.DeadLetter = true;
            item.NextAttemptAt = null;
        }
        else
        {
            item.NextAttemptAt = nextAttemptAt;
        }

        return new ValueTask<Result>(Result.Success());
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<OutboxEntry>> GetDeadLetterEventsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<OutboxEntry> deadLetter = _scopedItems.Values
            .SelectMany(items => items)
            .Where(i => i.DeadLetter)
            .Select(i => new OutboxEntry(i.Id, i.Event))
            .ToList();
        return new ValueTask<IReadOnlyList<OutboxEntry>>(deadLetter);
    }

    /// <inheritdoc />
    public ValueTask<int?> GetAttemptCountAsync(Guid id,
        CancellationToken cancellationToken = default)
    {
        if (!TryFindItem(id, out var item))
        {
            return new ValueTask<int?>((int?)null);
        }

        return new ValueTask<int?>(item.Attempts);
    }

    /// <inheritdoc />
    public ValueTask<int> GetPendingCountAsync(CancellationToken cancellationToken = default)
    {
        var count = _scopedItems.Values
            .SelectMany(items => items)
            .Count(i => i is { Processed: false, DeadLetter: false });
        return new ValueTask<int>(count);
    }

    /// <inheritdoc />
    public ValueTask<int> GetRetryingCountAsync(CancellationToken cancellationToken = default)
    {
        var count = _scopedItems.Values
            .SelectMany(items => items)
            .Count(i => i is { Processed: false, DeadLetter: false } && i.Attempts > 0);
        return new ValueTask<int>(count);
    }

    /// <inheritdoc />
    public ValueTask<int> GetDeadLetterCountAsync(CancellationToken cancellationToken = default)
    {
        var count = _scopedItems.Values
            .SelectMany(items => items)
            .Count(i => i.DeadLetter);
        return new ValueTask<int>(count);
    }

    /// <inheritdoc />
    public ValueTask<TimeSpan?> GetOldestPendingAgeAsync(CancellationToken cancellationToken = default)
    {
        var oldestItem = _scopedItems.Values
            .SelectMany(items => items)
            .Where(i => i is { Processed: false, DeadLetter: false })
            .OrderBy(i => i.CreatedAt)
            .FirstOrDefault();

        if (oldestItem == null)
        {
            return new ValueTask<TimeSpan?>((TimeSpan?)null);
        }

        var age = DateTimeOffset.UtcNow - oldestItem.CreatedAt;
        return new ValueTask<TimeSpan?>(age);
    }

    private bool TryFindItem(Guid id, out Item item)
    {
        foreach (var scopedItems in _scopedItems.Values)
        {
            var foundItem = scopedItems.FirstOrDefault(i => i.Id == id);
            if (foundItem != null)
            {
                item = foundItem;
                return true;
            }
        }

        item = null!;
        return false;
    }


    private sealed record Item
    {
        public Item(IEvent @event)
        {
            Event = @event;
            Processed = false;
            CreatedAt = DateTimeOffset.UtcNow;
        }

        public Guid Id { get; } = Guid.NewGuid();
        public IEvent Event { get; }
        public bool Processed { get; set; }
        public DateTimeOffset CreatedAt { get; }
        public DateTimeOffset? ProcessedAt { get; set; }
        public int Attempts { get; set; }
        public string? LastError { get; set; }
        public bool DeadLetter { get; set; }
        public DateTimeOffset? NextAttemptAt { get; set; }
    }
}