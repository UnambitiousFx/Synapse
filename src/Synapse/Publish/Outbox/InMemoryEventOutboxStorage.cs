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
///         allowing it to be registered as a Singleton while maintaining scope isolation.
///         Each scope (request, message, etc.) has its own event collection identified by its CorrelationId.
///     </para>
/// </remarks>
/// <threadsafety>
///     This class is thread-safe and can be safely accessed from multiple threads concurrently.
/// </threadsafety>
public sealed class InMemoryEventOutboxStorage : IEventOutboxStorage
{
    private readonly ConcurrentDictionary<Guid, ConcurrentBag<Item>> _scopedItems = new();

    /// <inheritdoc />
    public ValueTask<IEnumerable<IEvent>> GetPendingEventsAsync(CancellationToken cancellationToken = default)
    {
        // Returns events ready for dispatch (not processed, not dead-letter, and past scheduled time)
        var now = DateTimeOffset.UtcNow;
        var correlationId = CorrelationContext.CurrentCorrelationId;

        if (!_scopedItems.TryGetValue(correlationId, out var items))
            return new ValueTask<IEnumerable<IEvent>>([]);

        return new ValueTask<IEnumerable<IEvent>>(items
            .Where(item =>
                item is { Processed: false, DeadLetter: false } &&
                (item.NextAttemptAt is null || item.NextAttemptAt <= now))
            .Select(item => item.Event)
            .ToList());
    }

    /// <inheritdoc />
    public ValueTask<Result> MarkAsProcessedAsync(IEvent @event,
        CancellationToken cancellationToken = default)
    {
        var correlationId = CorrelationContext.CurrentCorrelationId;

        if (!_scopedItems.TryGetValue(correlationId, out var items))
            return new ValueTask<Result>(Result.Failure($"Event '{@event}' was not found in the outbox storage"));

        var item = items.FirstOrDefault(i => i.Event.Equals(@event));
        if (item == null)
            return new ValueTask<Result>(Result.Failure($"Event '{@event}' was not found in the outbox storage"));

        item.Processed = true;
        item.ProcessedAt = DateTimeOffset.UtcNow;
        item.LastError = null;
        item.NextAttemptAt = null;
        return new ValueTask<Result>(Result.Success());
    }

    /// <inheritdoc />
    public ValueTask<Result> ClearAsync(CancellationToken cancellationToken = default)
    {
        var correlationId = CorrelationContext.CurrentCorrelationId;
        _scopedItems.TryRemove(correlationId, out _);
        return new ValueTask<Result>(Result.Success());
    }

    /// <inheritdoc />
    public ValueTask<Result> AddAsync<TEvent>(TEvent @event,
        CancellationToken cancellationToken = default)
        where TEvent : class, IEvent
    {
        // Default to LocalOnly for backward compatibility
        return AddAsync(@event, DistributionMode.Local, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<Result> AddAsync<TEvent>(TEvent @event,
        DistributionMode distributionMode,
        CancellationToken cancellationToken = default)
        where TEvent : class, IEvent
    {
        var correlationId = CorrelationContext.CurrentCorrelationId;
        var items = _scopedItems.GetOrAdd(correlationId, _ => new ConcurrentBag<Item>());
        items.Add(new Item(@event, distributionMode));

        return new ValueTask<Result>(Result.Success());
    }

    /// <inheritdoc />
    public ValueTask<Result> MarkAsFailedAsync(IEvent @event,
        string reason,
        bool deadLetter,
        DateTimeOffset? nextAttemptAt = null,
        CancellationToken cancellationToken = default)
    {
        var correlationId = CorrelationContext.CurrentCorrelationId;

        if (!_scopedItems.TryGetValue(correlationId, out var items))
            return new ValueTask<Result>(Result.Failure($"Event '{@event}' was not found in the outbox storage"));

        var item = items.FirstOrDefault(i => i.Event.Equals(@event));
        if (item == null)
            return new ValueTask<Result>(Result.Failure($"Event '{@event}' was not found in the outbox storage"));

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
    public ValueTask<IEnumerable<IEvent>> GetDeadLetterEventsAsync(CancellationToken cancellationToken = default)
    {
        var correlationId = CorrelationContext.CurrentCorrelationId;

        if (!_scopedItems.TryGetValue(correlationId, out var items))
            return new ValueTask<IEnumerable<IEvent>>(Array.Empty<IEvent>());

        return new ValueTask<IEnumerable<IEvent>>(items.Where(i => i.DeadLetter).Select(i => i.Event).ToList());
    }

    /// <inheritdoc />
    public ValueTask<int?> GetAttemptCountAsync(IEvent @event,
        CancellationToken cancellationToken = default)
    {
        var correlationId = CorrelationContext.CurrentCorrelationId;

        if (!_scopedItems.TryGetValue(correlationId, out var items))
            return new ValueTask<int?>((int?)null);

        return new ValueTask<int?>(items.FirstOrDefault(i => i.Event.Equals(@event))?.Attempts);
    }

    /// <inheritdoc />
    public ValueTask<DistributionMode> GetDistributionModeAsync(IEvent @event,
        CancellationToken cancellationToken = default)
    {
        var correlationId = CorrelationContext.CurrentCorrelationId;

        if (!_scopedItems.TryGetValue(correlationId, out var items))
            return new ValueTask<DistributionMode>(DistributionMode.Local);

        var item = items.FirstOrDefault(i => i.Event.Equals(@event));
        // Return LocalOnly as default if event not found (defensive programming)
        return new ValueTask<DistributionMode>(item?.DistributionMode ?? DistributionMode.Local);
    }

    private sealed record Item
    {
        public Item(IEvent @event, DistributionMode distributionMode = DistributionMode.Local)
        {
            Event = @event;
            DistributionMode = distributionMode;
            Processed = false;
            CreatedAt = DateTimeOffset.UtcNow;
        }

        public IEvent Event { get; }
        public DistributionMode DistributionMode { get; }
        public bool Processed { get; set; }
        public DateTimeOffset CreatedAt { get; }
        public DateTimeOffset? ProcessedAt { get; set; }
        public int Attempts { get; set; }
        public string? LastError { get; set; }
        public bool DeadLetter { get; set; }
        public DateTimeOffset? NextAttemptAt { get; set; }
    }
}