using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace UnambitiousFx.Synapse.Outbox.EntityFrameworkCore.Tests.Support;

/// <summary>
///     A shared-cache SQLite in-memory database holding both the business table and the outbox table, plus the
///     container wiring <see cref="EfCoreTransactionManager" /> needs: one Scoped connection shared by a
///     <see cref="BusinessDbContext" /> and an <see cref="OutboxDbContext" />.
/// </summary>
public sealed class TransactionTestDatabase : IAsyncDisposable
{
    private readonly SqliteInMemoryDatabase _database;

    private TransactionTestDatabase(SqliteInMemoryDatabase database)
    {
        _database = database;
    }

    public string ConnectionString => _database.ConnectionString;

    public static async Task<TransactionTestDatabase> CreateAsync(CancellationToken cancellationToken)
    {
        var database = await SqliteInMemoryDatabase.CreateAsync(cancellationToken);
        await using var business = new BusinessDbContext(new DbContextOptionsBuilder<BusinessDbContext>()
            .UseSqlite(database.ConnectionString).Options);
        // EnsureCreated no-ops once the outbox table exists, so the business table comes from its script.
        await business.Database.ExecuteSqlRawAsync(business.Database.GenerateCreateScript(), cancellationToken);
        return new TransactionTestDatabase(database);
    }

    /// <summary>
    ///     Registers a Scoped <see cref="SqliteConnection" /> and both contexts on it, and enlists both in the
    ///     transaction manager, business context first.
    /// </summary>
    public IServiceCollection AddContexts(IServiceCollection services)
    {
        services.AddScoped(_ => new SqliteConnection(_database.ConnectionString));
        services.AddDbContext<BusinessDbContext>((sp, o) => o.UseSqlite(sp.GetRequiredService<SqliteConnection>()));
        services.AddDbContext<OutboxDbContext>((sp, o) => o.UseSqlite(sp.GetRequiredService<SqliteConnection>()));
        services.AddEfCoreTransactions(t => t.Enlist<BusinessDbContext>().Enlist<OutboxDbContext>());
        return services;
    }

    /// <summary>The names of the business records committed so far, read on a fresh connection.</summary>
    public async Task<List<string>> CommittedRecordsAsync(CancellationToken cancellationToken)
    {
        await using var context = new BusinessDbContext(new DbContextOptionsBuilder<BusinessDbContext>()
            .UseSqlite(_database.ConnectionString).Options);
        return await context.Records.OrderBy(r => r.Name).Select(r => r.Name).ToListAsync(cancellationToken);
    }

    /// <summary>The outbox rows committed so far, read on a fresh connection.</summary>
    public async Task<List<OutboxEntity>> CommittedOutboxRowsAsync(CancellationToken cancellationToken)
    {
        await using var context = _database.CreateContext();
        return await context.OutboxEvents.AsNoTracking().ToListAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        return _database.DisposeAsync();
    }
}
