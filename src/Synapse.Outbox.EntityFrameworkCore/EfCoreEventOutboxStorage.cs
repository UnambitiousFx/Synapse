using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Outbox.EntityFrameworkCore;

/// <summary>
///     Persists outbox entries through an EF Core <see cref="DbContext" />, so a store lands in
///     whatever transaction that context is already enlisted in.
/// </summary>
/// <remarks>
///     <para>
///         Register this type Scoped — the default when wired via
///         <c>ISynapseConfig.SetEventOutboxStorage&lt;EfCoreEventOutboxStorage&lt;TContext&gt;&gt;()</c> —
///         and constructed with the same <typeparamref name="TContext" /> instance the rest of the
///         current scope uses. That is what lets a stored entry share the caller's transaction: this
///         storage never opens a transaction of its own, it only calls
///         <see cref="DbContext.SaveChangesAsync(CancellationToken)" /> on the context it was given.
///     </para>
///     <para>
///         Sharing atomicity across several <see cref="DbContext" />s (a modular monolith with one
///         business context per module plus this one) is done by sharing one <c>DbTransaction</c>
///         across them via <c>Database.UseTransactionAsync</c> before either context saves.
///     </para>
/// </remarks>
/// <typeparam name="TContext">The <see cref="DbContext" /> type that owns the outbox table.</typeparam>
public sealed class EfCoreEventOutboxStorage<TContext> : IEventOutboxStorage, IDiscardableOutboxStorage
    where TContext : DbContext
{
    private readonly TContext _context;
    private readonly Dictionary<IEvent, Guid> _storedByReference = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    ///     Initializes the storage with the <see cref="DbContext" /> whose transaction outbox writes
    ///     enlist in.
    /// </summary>
    /// <param name="context">The EF Core context that owns the outbox table.</param>
    public EfCoreEventOutboxStorage(TContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async ValueTask<Result> AddAsync<TEvent>(TEvent @event,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken = default)
        where TEvent : class, IEvent
    {
        var entity = new OutboxEntity
        {
            Id = Guid.NewGuid(),
            EventType = typeof(TEvent).AssemblyQualifiedName!,
            Payload = JsonSerializer.Serialize(@event, typeof(TEvent)),
            Headers = JsonSerializer.Serialize(headers),
            CreatedAt = DateTimeOffset.UtcNow
        };

        _context.Set<OutboxEntity>().Add(entity);
        var saveResult = await TrySaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
        {
            return saveResult;
        }

        _storedByReference[@event] = entity.Id;
        return Result.Success();
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<OutboxEntry>> GetPendingEventsAsync(
        CancellationToken cancellationToken = default)
    {
        // The NextAttemptAt filter/ordering below run client-side (after the SQL round trip) rather
        // than being pushed into the query: the SQLite provider cannot translate ORDER BY, or a WHERE
        // predicate combining a boolean column with a DateTimeOffset comparison, over a
        // DateTimeOffset column (a longstanding EF Core Sqlite provider limitation). The boolean
        // filters below are safely translated for every provider, keeping the SQL-side filtering as
        // selective as possible.
        var now = DateTimeOffset.UtcNow;
        var rows = await _context.Set<OutboxEntity>()
            .Where(e => !e.Processed && !e.DeadLetter && !e.Discarded)
            .ToListAsync(cancellationToken);

        return rows
            .Where(e => e.NextAttemptAt == null || e.NextAttemptAt <= now)
            .OrderBy(e => e.CreatedAt)
            .Select(ToOutboxEntry)
            .ToList();
    }

    /// <inheritdoc />
    public async ValueTask<Result> MarkAsProcessedAsync(Guid id,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.Set<OutboxEntity>().FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (entity is null)
        {
            return Result.Failure($"Outbox item '{id}' was not found in the outbox storage");
        }

        entity.Processed = true;
        entity.ProcessedAt = DateTimeOffset.UtcNow;
        entity.LastError = null;
        entity.NextAttemptAt = null;

        return await TrySaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<Result> ClearAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _context.Set<OutboxEntity>().ExecuteDeleteAsync(cancellationToken);
            return Result.Success();
        }
        catch (DbUpdateException ex)
        {
            return Result.Failure(ex.Message);
        }
    }

    /// <inheritdoc />
    public async ValueTask<Result> MarkAsFailedAsync(Guid id,
        string reason,
        bool deadLetter,
        DateTimeOffset? nextAttemptAt = null,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.Set<OutboxEntity>().FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (entity is null)
        {
            return Result.Failure($"Outbox item '{id}' was not found in the outbox storage");
        }

        entity.Attempts++;
        entity.LastError = reason;
        if (deadLetter)
        {
            entity.DeadLetter = true;
            entity.NextAttemptAt = null;
        }
        else
        {
            entity.NextAttemptAt = nextAttemptAt;
        }

        return await TrySaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<OutboxEntry>> GetDeadLetterEventsAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await _context.Set<OutboxEntity>().Where(e => e.DeadLetter).ToListAsync(cancellationToken);
        return rows.Select(ToOutboxEntry).ToList();
    }

    /// <inheritdoc />
    public async ValueTask<int?> GetAttemptCountAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _context.Set<OutboxEntity>().FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        return entity?.Attempts;
    }

    /// <inheritdoc />
    public async ValueTask<int> GetPendingCountAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Set<OutboxEntity>()
            .CountAsync(e => !e.Processed && !e.DeadLetter && !e.Discarded, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<int> GetRetryingCountAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Set<OutboxEntity>()
            .CountAsync(e => !e.Processed && !e.DeadLetter && !e.Discarded && e.Attempts > 0, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<int> GetDeadLetterCountAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Set<OutboxEntity>().CountAsync(e => e.DeadLetter, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<TimeSpan?> GetOldestPendingAgeAsync(CancellationToken cancellationToken = default)
    {
        // Ordering by CreatedAt (a DateTimeOffset column) runs client-side for the same reason as in
        // GetPendingEventsAsync — the SQLite provider cannot translate ORDER BY over DateTimeOffset.
        var createdAtValues = await _context.Set<OutboxEntity>()
            .Where(e => !e.Processed && !e.DeadLetter && !e.Discarded)
            .Select(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

        if (createdAtValues.Count == 0)
        {
            return null;
        }

        return DateTimeOffset.UtcNow - createdAtValues.Min();
    }

    /// <inheritdoc />
    public async ValueTask<Result> DiscardAsync(IReadOnlyCollection<IEvent> events,
        CancellationToken cancellationToken = default)
    {
        var ids = new List<Guid>();
        foreach (var @event in events)
        {
            if (_storedByReference.TryGetValue(@event, out var id))
            {
                ids.Add(id);
            }
        }

        if (ids.Count == 0)
        {
            return Result.Success();
        }

        var entities = await _context.Set<OutboxEntity>()
            .Where(e => ids.Contains(e.Id) && !e.Processed && !e.DeadLetter)
            .ToListAsync(cancellationToken);

        foreach (var entity in entities)
        {
            entity.Discarded = true;
        }

        return await TrySaveChangesAsync(cancellationToken);
    }

    private async ValueTask<Result> TrySaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DbUpdateException ex)
        {
            return Result.Failure(ex.Message);
        }
    }

    private static OutboxEntry ToOutboxEntry(OutboxEntity entity)
    {
        var type = Type.GetType(entity.EventType, throwOnError: true)!;
        var @event = (IEvent)JsonSerializer.Deserialize(entity.Payload, type)!;
        var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(entity.Headers)
                      ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return new OutboxEntry(entity.Id, @event, headers);
    }
}
