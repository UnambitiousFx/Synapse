using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Outbox.EntityFrameworkCore;

/// <summary>
///     <see cref="ITransactionManager" /> over one or more EF Core <see cref="DbContext" />s that share one
///     connection: a single <see cref="DbTransaction" /> spans all of them, and a scope opened while a transaction
///     is already open becomes a savepoint inside it.
/// </summary>
/// <remarks>
///     <para>
///         Register it with <see cref="ServiceCollectionExtensions.AddEfCoreTransactions" />, then
///         <c>AddSynapse(cfg =&gt; cfg.AddTransactions&lt;EfCoreTransactionManager&gt;())</c>. It is Scoped: the
///         contexts it enlists are the ones of the current scope, the same instances the handlers receive.
///     </para>
///     <para>
///         The first enlisted context opens the transaction and the others join it with
///         <c>Database.UseTransactionAsync</c>, which requires them to use the very same
///         <see cref="DbConnection" /> instance. When that first context already has a transaction, whether a
///         Synapse scope or one the application opened itself, the new scope is a savepoint: committing it
///         releases the savepoint, disposing it without a commit rolls back to it, and the enclosing transaction
///         stays open.
///     </para>
///     <para>
///         Once the outermost transaction commits, the pending outbox events are dispatched through
///         <see cref="IOutboxCommit.CommitAsync" /> (see <see cref="EfCoreTransactionOptions.FlushOutboxOnCommit" />).
///         That runs after the commit, not inside the transaction: each event is dispatched in a fresh scope on its
///         own connection, which can only see committed rows. Enlisting the outbox context is what puts the stored
///         events in the same transaction as the business data.
///     </para>
///     <para>
///         A rollback reverts the database, not the change trackers: entities a rolled-back scope saved stay
///         tracked as unchanged. Do not keep using them in the same scope after a failure.
///     </para>
/// </remarks>
public sealed class EfCoreTransactionManager : ITransactionManager
{
    private static long _savepointCounter;

    private readonly EfCoreTransactionOptions _options;
    private readonly IServiceProvider _services;
    private DbContext[]? _contexts;
    private int _depth;

    /// <summary>
    ///     Creates the manager.
    /// </summary>
    /// <param name="services">The scope's service provider, used to resolve the enlisted contexts.</param>
    /// <param name="options">The contexts to enlist and the outbox flush setting.</param>
    public EfCoreTransactionManager(IServiceProvider services, EfCoreTransactionOptions options)
    {
        _services = services;
        _options = options;
    }

    /// <inheritdoc />
    public async ValueTask<ITransactionScope> BeginAsync(CancellationToken cancellationToken = default)
    {
        var contexts = _contexts ??= ResolveContexts();
        var primary = contexts[0];

        if (primary.Database.CurrentTransaction is { } current)
        {
            return await BeginSavepointAsync(contexts, current, cancellationToken);
        }

        var transaction = await primary.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var joined = await JoinAsync(contexts, transaction.GetDbTransaction(), cancellationToken);
            return new RootScope(this, transaction, joined, ++_depth);
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    private async ValueTask<ITransactionScope> BeginSavepointAsync(DbContext[] contexts,
        IDbContextTransaction current,
        CancellationToken cancellationToken)
    {
        // Contexts already sharing the transaction are skipped; only a transaction the application opened on the
        // first context alone leaves some to join here.
        var joined = await JoinAsync(contexts, current.GetDbTransaction(), cancellationToken);
        var name = $"synapse_{Interlocked.Increment(ref _savepointCounter)}";
        try
        {
            await current.CreateSavepointAsync(name, cancellationToken);
        }
        catch
        {
            await LeaveAsync(joined);
            throw;
        }

        return new SavepointScope(this, current, name, joined, ++_depth);
    }

    private DbContext[] ResolveContexts()
    {
        if (_options.ContextTypes.Count == 0)
        {
            throw new InvalidOperationException(
                $"{nameof(EfCoreTransactionManager)} has no DbContext to open a transaction on. Enlist at least " +
                $"one with services.AddEfCoreTransactions(t => t.{nameof(EfCoreTransactionOptions.Enlist)}<TContext>()).");
        }

        var contexts = new DbContext[_options.ContextTypes.Count];
        for (var i = 0; i < contexts.Length; i++)
        {
            contexts[i] = (DbContext)_services.GetRequiredService(_options.ContextTypes[i]);
        }

        return contexts;
    }

    /// <summary>
    ///     Enlists every context after the first in <paramref name="transaction" />, returning the ones this call
    ///     enlisted so the scope that owns them can release them.
    /// </summary>
    private static async ValueTask<List<DbContext>> JoinAsync(DbContext[] contexts,
        DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        var joined = new List<DbContext>(contexts.Length - 1);
        try
        {
            for (var i = 1; i < contexts.Length; i++)
            {
                var database = contexts[i].Database;
                if (database.CurrentTransaction is { } existing)
                {
                    if (!ReferenceEquals(existing.GetDbTransaction(), transaction))
                    {
                        throw new InvalidOperationException(
                            $"{contexts[i].GetType().Name} is already enlisted in another transaction than " +
                            $"{contexts[0].GetType().Name}'s, so they cannot commit or roll back together.");
                    }

                    continue;
                }

                if (!ReferenceEquals(database.GetDbConnection(), transaction.Connection))
                {
                    throw new InvalidOperationException(
                        $"{contexts[i].GetType().Name} does not use the same DbConnection instance as " +
                        $"{contexts[0].GetType().Name}, so it cannot join its transaction. Register one Scoped " +
                        "connection and pass it to every enlisted context, e.g. " +
                        "AddDbContext<TContext>((sp, o) => o.UseNpgsql(sp.GetRequiredService<NpgsqlConnection>())).");
                }

                await database.UseTransactionAsync(transaction, cancellationToken);
                joined.Add(contexts[i]);
            }
        }
        catch
        {
            await LeaveAsync(joined);
            throw;
        }

        return joined;
    }

    private static async ValueTask LeaveAsync(List<DbContext> joined)
    {
        foreach (var context in joined)
        {
            await context.Database.UseTransactionAsync(null);
        }
    }

    private void Complete(int depth)
    {
        if (depth != _depth)
        {
            throw new InvalidOperationException(
                "A transaction scope was completed while a scope nested inside it was still open. Complete " +
                "nested scopes first.");
        }

        _depth--;
    }

    private async ValueTask FlushOutboxAsync(CancellationToken cancellationToken)
    {
        // A canceled request leaves the stored events pending for the next commit or the background dispatcher;
        // the data they describe is already committed.
        if (!_options.FlushOutboxOnCommit || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (_services.GetService<IOutboxCommit>() is { } outboxCommit)
        {
            // A failed dispatch is recorded by the outbox itself (retry, dead letter), and the transaction has
            // committed, so the request's own outcome does not change.
            await outboxCommit.CommitAsync(cancellationToken);
        }
    }

    private sealed class RootScope(
        EfCoreTransactionManager manager,
        IDbContextTransaction transaction,
        List<DbContext> joined,
        int depth) : ITransactionScope
    {
        private bool _completed;

        public async ValueTask CommitAsync(CancellationToken cancellationToken = default)
        {
            if (_completed)
            {
                throw new InvalidOperationException("The transaction scope has already been completed.");
            }

            manager.Complete(depth);
            _completed = true;
            try
            {
                await transaction.CommitAsync(cancellationToken);
            }
            finally
            {
                await ReleaseAsync();
            }

            await manager.FlushOutboxAsync(cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            if (_completed)
            {
                return;
            }

            _completed = true;
            // Depth is only rebalanced here: an out-of-order dispose still rolls back everything it covers.
            manager._depth = depth - 1;
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            finally
            {
                await ReleaseAsync();
            }
        }

        private async ValueTask ReleaseAsync()
        {
            try
            {
                await LeaveAsync(joined);
            }
            finally
            {
                await transaction.DisposeAsync();
            }
        }
    }

    private sealed class SavepointScope(
        EfCoreTransactionManager manager,
        IDbContextTransaction transaction,
        string name,
        List<DbContext> joined,
        int depth) : ITransactionScope
    {
        private bool _completed;

        public async ValueTask CommitAsync(CancellationToken cancellationToken = default)
        {
            if (_completed)
            {
                throw new InvalidOperationException("The transaction scope has already been completed.");
            }

            manager.Complete(depth);
            _completed = true;
            try
            {
                await transaction.ReleaseSavepointAsync(name, cancellationToken);
            }
            finally
            {
                await LeaveAsync(joined);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_completed)
            {
                return;
            }

            _completed = true;
            manager._depth = depth - 1;
            try
            {
                await transaction.RollbackToSavepointAsync(name, CancellationToken.None);
            }
            finally
            {
                await LeaveAsync(joined);
            }
        }
    }
}
