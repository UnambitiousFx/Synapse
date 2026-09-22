using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace UnambitiousFx.Synapse.Outbox.EntityFrameworkCore.Tests.Support;

/// <summary>
///     A shared-cache SQLite in-memory database that stays alive for as long as one connection to it
///     is kept open, so multiple <see cref="OutboxDbContext"/> instances (or connections) can see the
///     same data — used to prove real transactional isolation without an external database.
/// </summary>
public sealed class SqliteInMemoryDatabase : IAsyncDisposable
{
    private readonly SqliteConnection _keepAliveConnection;

    public string ConnectionString { get; }

    private SqliteInMemoryDatabase(string connectionString, SqliteConnection keepAliveConnection)
    {
        ConnectionString = connectionString;
        _keepAliveConnection = keepAliveConnection;
    }

    public static async Task<SqliteInMemoryDatabase> CreateAsync(CancellationToken cancellationToken)
    {
        var name = $"outbox-tests-{Guid.NewGuid():N}";
        var connectionString = $"DataSource=file:{name}?mode=memory&cache=shared";
        var keepAlive = new SqliteConnection(connectionString);
        await keepAlive.OpenAsync(cancellationToken);

        var database = new SqliteInMemoryDatabase(connectionString, keepAlive);
        await using var context = database.CreateContext();
        await context.Database.EnsureCreatedAsync(cancellationToken);
        return database;
    }

    public OutboxDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<OutboxDbContext>()
            .UseSqlite(ConnectionString)
            .Options;
        return new OutboxDbContext(options);
    }

    public async ValueTask DisposeAsync()
    {
        await _keepAliveConnection.DisposeAsync();
    }
}
