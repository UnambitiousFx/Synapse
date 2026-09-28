using System.Data;
using System.Data.Common;
using System.Text.Json;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Outbox.AdoNet;

/// <summary>
///     Persists outbox entries with plain ADO.NET, so a store can join the caller's own database transaction
///     without Entity Framework Core.
/// </summary>
/// <remarks>
///     <para>
///         Register it with <see cref="ServiceCollectionExtensions.AddAdoNetEventOutbox" /> and make it the active
///         storage with <c>ISynapseConfig.SetEventOutboxStorage&lt;AdoNetEventOutboxStorage&gt;()</c>. Connections
///         come from the <see cref="DbDataSource" /> registered in DI, which is where the choice of driver (Npgsql,
///         Microsoft.Data.SqlClient) is made.
///     </para>
///     <para>
///         <see cref="AddAsync{TEvent}" /> enlists in the transaction set on the scope's
///         <see cref="AdoNetOutboxTransaction" />, so the entry commits or rolls back with the business change.
///         Every other operation runs on a connection of its own: reading, claiming and marking entries happens
///         after that transaction has committed, typically from a different scope altogether.
///     </para>
/// </remarks>
public sealed class AdoNetEventOutboxStorage : IEventOutboxStorage, IDiscardableOutboxStorage, IClaimableOutboxStorage
{
    private readonly DbDataSource _dataSource;
    private readonly OutboxStatements _sql;

    // Maps an event instance (by reference) to every row this storage instance created for it, so DiscardAsync
    // can take them all back — matching InMemoryEventOutboxStorage.DiscardAsync.
    private readonly Dictionary<IEvent, List<Guid>> _storedByReference = new(ReferenceEqualityComparer.Instance);
    private readonly AdoNetOutboxTransaction _transaction;

    /// <summary>
    ///     Initializes the storage.
    /// </summary>
    /// <param name="dataSource">Opens the connections the storage uses outside the caller's transaction.</param>
    /// <param name="transaction">The scope's transaction, which <see cref="AddAsync{TEvent}" /> enlists in when set.</param>
    /// <param name="options">The dialect and the location of the outbox table.</param>
    public AdoNetEventOutboxStorage(DbDataSource dataSource,
        AdoNetOutboxTransaction transaction,
        AdoNetOutboxOptions options)
    {
        _dataSource = dataSource;
        _transaction = transaction;
        _sql = new OutboxStatements(options);
    }

    /// <inheritdoc />
    public async ValueTask<Result> AddAsync<TEvent>(TEvent @event,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken = default)
        where TEvent : class, IEvent
    {
        var id = Guid.NewGuid();
        try
        {
            var transaction = _transaction.Current;
            if (transaction is not null)
            {
                // Falling back to a connection of its own would silently store the entry outside the transaction
                // the caller asked for. Drivers report a completed transaction differently: SqlClient clears its
                // Connection, while Npgsql keeps it and throws InvalidOperationException once it is used.
                if (transaction.Connection is not { } connection)
                {
                    return CompletedTransactionFailure(
                        "The outbox transaction enlisted in this scope has already been committed or rolled back.");
                }

                try
                {
                    await using var command = CreateInsert(connection, @event, headers, id);
                    command.Transaction = transaction;
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }
                catch (InvalidOperationException ex)
                {
                    // Most likely a completed transaction, but the driver's own message is kept: the same exception
                    // type also covers other invalid connection states.
                    return CompletedTransactionFailure(
                        $"Could not store the entry in the outbox transaction enlisted in this scope: {ex.Message}");
                }
            }
            else
            {
                await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
                await using var command = CreateInsert(connection, @event, headers, id);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        catch (DbException ex)
        {
            return Result.Failure(ex.Message);
        }

        lock (_storedByReference)
        {
            if (!_storedByReference.TryGetValue(@event, out var ids))
            {
                ids = [];
                _storedByReference[@event] = ids;
            }

            ids.Add(id);
        }

        return Result.Success();
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Each entry's event is rehydrated from its stored assembly-qualified type name. If an event type was
    ///     renamed, moved or deleted while rows referencing it are pending, this call throws, blocking every pending
    ///     entry until an operator fixes or deletes the row.
    /// </remarks>
    public async ValueTask<IReadOnlyList<OutboxEntry>> GetPendingEventsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, _sql.SelectClaimable);
        AddParameter(command, "now", DateTimeOffset.UtcNow, DbType.DateTimeOffset);
        return await ReadEntriesAsync(command, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     A single statement locks the candidate rows, skipping rows another claimer has locked
    ///     (<c>FOR UPDATE SKIP LOCKED</c> on PostgreSQL, <c>UPDLOCK, READPAST</c> on SQL Server), stamps the lease on
    ///     them and returns them. Concurrent claimers therefore split the backlog rather than contend for it.
    /// </remarks>
    public async ValueTask<IReadOnlyList<OutboxEntry>> ClaimPendingEventsAsync(int? maxCount,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, maxCount.HasValue ? _sql.ClaimLimited : _sql.Claim);
        AddParameter(command, "now", now, DbType.DateTimeOffset);
        AddParameter(command, "claimed_until", now + leaseDuration, DbType.DateTimeOffset);
        AddParameter(command, "claim_token", Guid.NewGuid(), DbType.Guid);
        if (maxCount.HasValue)
        {
            AddParameter(command, "max_count", maxCount.Value, DbType.Int32);
        }

        return await ReadEntriesAsync(command, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<Result> MarkAsProcessedAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await ExecuteOnRowAsync(id, _sql.MarkProcessed, command =>
            AddParameter(command, "now", DateTimeOffset.UtcNow, DbType.DateTimeOffset), cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<Result> MarkAsFailedAsync(Guid id,
        string reason,
        bool deadLetter,
        DateTimeOffset? nextAttemptAt = null,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteOnRowAsync(id, deadLetter ? _sql.MarkDeadLetter : _sql.MarkFailed, command =>
        {
            AddParameter(command, "last_error", reason, DbType.String);
            if (!deadLetter)
            {
                AddParameter(command, "next_attempt_at", nextAttemptAt, DbType.DateTimeOffset);
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<Result> ClearAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = CreateCommand(connection, _sql.Clear);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return Result.Success();
        }
        catch (DbException ex)
        {
            return Result.Failure(ex.Message);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Carries the same hazard as <see cref="GetPendingEventsAsync" /> when a stored event type no longer
    ///     resolves.
    /// </remarks>
    public async ValueTask<IReadOnlyList<OutboxEntry>> GetDeadLetterEventsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, _sql.SelectDeadLetter);
        return await ReadEntriesAsync(command, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<int?> GetAttemptCountAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, _sql.SelectAttempts);
        AddParameter(command, "id", id, DbType.Guid);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? null : Convert.ToInt32(value);
    }

    /// <inheritdoc />
    public ValueTask<int> GetPendingCountAsync(CancellationToken cancellationToken = default)
    {
        return CountAsync(_sql.CountPending, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<int> GetRetryingCountAsync(CancellationToken cancellationToken = default)
    {
        return CountAsync(_sql.CountRetrying, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<int> GetDeadLetterCountAsync(CancellationToken cancellationToken = default)
    {
        return CountAsync(_sql.CountDeadLetter, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<TimeSpan?> GetOldestPendingAgeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, _sql.OldestPending);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || await reader.IsDBNullAsync(0, cancellationToken))
        {
            return null;
        }

        return DateTimeOffset.UtcNow - reader.GetFieldValue<DateTimeOffset>(0);
    }

    /// <inheritdoc />
    public async ValueTask<Result> DiscardAsync(IReadOnlyCollection<IEvent> events,
        CancellationToken cancellationToken = default)
    {
        var ids = new List<Guid>();
        lock (_storedByReference)
        {
            foreach (var @event in events)
            {
                if (_storedByReference.TryGetValue(@event, out var storedIds))
                {
                    ids.AddRange(storedIds);
                }
            }
        }

        if (ids.Count == 0)
        {
            return Result.Success();
        }

        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            var names = ids.Select((_, index) => $"@id{index}");
            await using var command = CreateCommand(connection, $"{_sql.DiscardPrefix} ({string.Join(", ", names)})");
            for (var index = 0; index < ids.Count; index++)
            {
                AddParameter(command, $"id{index}", ids[index], DbType.Guid);
            }

            await command.ExecuteNonQueryAsync(cancellationToken);
            return Result.Success();
        }
        catch (DbException ex)
        {
            return Result.Failure(ex.Message);
        }
    }

    private static Result CompletedTransactionFailure(string reason)
    {
        return Result.Failure(
            $"{reason} If it has completed, enlist the new transaction, or call AdoNetOutboxTransaction.Clear() to " +
            "store outside one.");
    }

    private DbCommand CreateInsert<TEvent>(DbConnection connection,
        TEvent @event,
        IReadOnlyDictionary<string, string> headers,
        Guid id)
        where TEvent : class, IEvent
    {
        var command = CreateCommand(connection, _sql.Insert);
        AddParameter(command, "id", id, DbType.Guid);
        AddParameter(command, "event_type", typeof(TEvent).AssemblyQualifiedName!, DbType.String);
        AddParameter(command, "payload", JsonSerializer.Serialize(@event, typeof(TEvent)), DbType.String);
        AddParameter(command, "headers", JsonSerializer.Serialize(headers), DbType.String);
        AddParameter(command, "created_at", DateTimeOffset.UtcNow, DbType.DateTimeOffset);
        return command;
    }

    private async ValueTask<Result> ExecuteOnRowAsync(Guid id,
        string sql,
        Action<DbCommand> addParameters,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = CreateCommand(connection, sql);
            AddParameter(command, "id", id, DbType.Guid);
            addParameters(command);
            var affected = await command.ExecuteNonQueryAsync(cancellationToken);
            return affected == 0
                ? Result.Failure($"Outbox item '{id}' was not found in the outbox storage")
                : Result.Success();
        }
        catch (DbException ex)
        {
            return Result.Failure(ex.Message);
        }
    }

    private async ValueTask<int> CountAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(value);
    }

    private static DbCommand CreateCommand(DbConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        return command;
    }

    private static void AddParameter(DbCommand command, string name, object? value, DbType type)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        // Npgsql only writes timestamptz from a zero-offset DateTimeOffset; normalizing keeps a caller-supplied
        // next-attempt time with any offset writable, and changes nothing on SQL Server.
        parameter.Value = value switch
        {
            null => DBNull.Value,
            DateTimeOffset timestamp => timestamp.ToUniversalTime(),
            _ => value
        };
        command.Parameters.Add(parameter);
    }

    private static async ValueTask<IReadOnlyList<OutboxEntry>> ReadEntriesAsync(DbCommand command,
        CancellationToken cancellationToken)
    {
        var rows = new List<(OutboxEntry Entry, DateTimeOffset CreatedAt)>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetGuid(0);
                var entry = ToOutboxEntry(id, reader.GetString(1), reader.GetString(2), reader.GetString(3));
                rows.Add((entry, reader.GetFieldValue<DateTimeOffset>(4)));
            }
        }

        // Sorted here rather than trusted to the statement: RETURNING and OUTPUT do not preserve an order.
        rows.Sort((left, right) =>
        {
            var byCreatedAt = left.CreatedAt.CompareTo(right.CreatedAt);
            return byCreatedAt != 0 ? byCreatedAt : left.Entry.Id.CompareTo(right.Entry.Id);
        });
        return rows.Select(row => row.Entry).ToList();
    }

    private static OutboxEntry ToOutboxEntry(Guid id, string eventType, string payload, string storedHeaders)
    {
        var type = Type.GetType(eventType, throwOnError: true)!;
        if (!typeof(IEvent).IsAssignableFrom(type))
        {
            throw new InvalidOperationException(
                $"Outbox item '{id}' names event type '{type.AssemblyQualifiedName}', which does not implement " +
                $"{nameof(IEvent)}. The stored row is corrupt or was written by a different schema version; fix " +
                "or remove it before processing the outbox.");
        }

        var @event = (IEvent)JsonSerializer.Deserialize(payload, type)!;

        // System.Text.Json does not round-trip a dictionary's comparer; headers are looked up case-insensitively
        // everywhere else, so rebuild them that way.
        var deserialized = JsonSerializer.Deserialize<Dictionary<string, string>>(storedHeaders);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (deserialized is not null)
        {
            foreach (var (key, value) in deserialized)
            {
                headers[key] = value;
            }
        }

        return new OutboxEntry(id, @event, headers);
    }
}
