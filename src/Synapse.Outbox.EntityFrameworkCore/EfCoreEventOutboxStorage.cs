using System.Data.Common;
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

    // Maps an event instance (by reference) to every outbox row this storage instance created for it.
    // A list rather than a single id because the same event reference can legitimately be added more
    // than once, and DiscardAsync must then take all of its rows back — matching
    // InMemoryEventOutboxStorage.DiscardAsync, which discards every stored item whose event reference
    // matches.
    private readonly Dictionary<IEvent, List<Guid>> _storedByReference = new(ReferenceEqualityComparer.Instance);

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
            // Detach the row that failed to save: the context is shared with the caller (that is the
            // whole point of this storage), so leaving it Added would make the caller's next
            // SaveChangesAsync silently re-attempt the very insert that just failed.
            _context.Entry(entity).State = EntityState.Detached;
            return saveResult;
        }

        if (!_storedByReference.TryGetValue(@event, out var ids))
        {
            ids = [];
            _storedByReference[@event] = ids;
        }

        ids.Add(entity.Id);
        return Result.Success();
    }

    /// <inheritdoc />
    /// <remarks>
    ///     <para>
    ///         Each returned entry's event is rehydrated from its stored assembly-qualified type name via
    ///         <see cref="Type.GetType(string, bool)" />. If an event type was renamed, moved to another
    ///         assembly or deleted after rows referencing it were written, that resolution throws and this
    ///         call fails as a whole — blocking dispatch of <em>every</em> pending entry, not only the
    ///         offending one. There is no in-library recovery: an operator has to fix the stored
    ///         <c>EventType</c> value (or delete the row) by hand. Keep a type-forwarding shim, or migrate
    ///         the stored type names, before renaming or removing an event type that may still have pending
    ///         outbox rows.
    ///     </para>
    ///     <para>
    ///         The <c>NextAttemptAt</c> filtering and <c>CreatedAt</c> ordering run client-side on every
    ///         provider — see the comment in the method body.
    ///     </para>
    /// </remarks>
    public async ValueTask<IReadOnlyList<OutboxEntry>> GetPendingEventsAsync(
        CancellationToken cancellationToken = default)
    {
        // Only the boolean-column filter is pushed down to SQL. The NextAttemptAt filter and the
        // CreatedAt ordering run client-side, after the round trip, on EVERY provider — not just
        // SQLite. The SQLite provider cannot translate ORDER BY, or a WHERE predicate combining a
        // boolean column with a DateTimeOffset comparison, over a DateTimeOffset column (a
        // longstanding EF Core Sqlite provider limitation); rather than branching per provider, the
        // same client-side shape is used everywhere for one behaviour that is correct on all of them.
        // Pushing the DateTimeOffset predicate down (e.g. via a ValueConverter to a sortable integer)
        // is tracked as a follow-up.
        var now = DateTimeOffset.UtcNow;
        var rows = await _context.Set<OutboxEntity>()
            .AsNoTracking()
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

            // ExecuteDeleteAsync goes straight to the database and does not touch the change tracker,
            // so the context can still be holding tracked instances of rows that no longer exist.
            // Dropping them keeps a later SaveChangesAsync from issuing UPDATEs against deleted rows.
            _context.ChangeTracker.Clear();
            return Result.Success();
        }
        catch (DbException ex)
        {
            // ExecuteDeleteAsync bypasses SaveChanges, so a provider failure surfaces as the provider's
            // own DbException rather than as DbUpdateException. Both are caught: DbUpdateException can
            // still come from EF's own pre-execution checks.
            return Result.Failure(ex.Message);
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
    /// <remarks>
    ///     Rehydrates each entry's event from its stored assembly-qualified type name, so it carries the
    ///     same hazard as <see cref="GetPendingEventsAsync" />: a single row naming a type that no longer
    ///     resolves makes this call throw and hides the whole dead-letter queue until an operator fixes or
    ///     deletes that row.
    /// </remarks>
    public async ValueTask<IReadOnlyList<OutboxEntry>> GetDeadLetterEventsAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await _context.Set<OutboxEntity>()
            .AsNoTracking()
            .Where(e => e.DeadLetter)
            .ToListAsync(cancellationToken);
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
        // The CreatedAt aggregation runs client-side, on every provider, for the same reason as in
        // GetPendingEventsAsync — the SQLite provider cannot translate ORDER BY or MIN over a
        // DateTimeOffset column, and one uniform shape is used across providers rather than branching.
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
        // Every row stored for a matched event reference is taken back, not just the most recent one,
        // matching InMemoryEventOutboxStorage.DiscardAsync.
        var ids = new List<Guid>();
        foreach (var @event in events)
        {
            if (_storedByReference.TryGetValue(@event, out var storedIds))
            {
                ids.AddRange(storedIds);
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
        if (!typeof(IEvent).IsAssignableFrom(type))
        {
            throw new InvalidOperationException(
                $"Outbox item '{entity.Id}' names event type '{type.AssemblyQualifiedName}', which does " +
                $"not implement {nameof(IEvent)}. The stored row is corrupt or was written by a " +
                "different schema version; fix or remove it before processing the outbox.");
        }

        var @event = (IEvent)JsonSerializer.Deserialize(entity.Payload, type)!;

        // System.Text.Json does not preserve a dictionary's IEqualityComparer across a
        // serialize/deserialize round trip, so the deserialized dictionary always comes back with the
        // default (ordinal, case-sensitive) comparer. Headers are HTTP-header-like (traceparent,
        // baggage) and looked up case-insensitively everywhere else in this codebase (OutboxEntry's own
        // default, InMemoryEventOutboxStorage), so copy into a case-insensitive dictionary here too.
        var deserializedHeaders = JsonSerializer.Deserialize<Dictionary<string, string>>(entity.Headers);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (deserializedHeaders is not null)
        {
            foreach (var (key, value) in deserializedHeaders)
            {
                headers[key] = value;
            }
        }

        return new OutboxEntry(entity.Id, @event, headers);
    }
}
