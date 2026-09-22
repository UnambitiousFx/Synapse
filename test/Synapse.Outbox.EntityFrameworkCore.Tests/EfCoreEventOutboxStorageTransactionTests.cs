using JetBrains.Annotations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using UnambitiousFx.Synapse.Outbox.EntityFrameworkCore.Tests.Support;

namespace UnambitiousFx.Synapse.Outbox.EntityFrameworkCore.Tests;

[TestSubject(typeof(EfCoreEventOutboxStorage<OutboxDbContext>))]
public sealed class EfCoreEventOutboxStorageTransactionTests
{
    private static readonly IReadOnlyDictionary<string, string> NoHeaders =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public async Task AddAsync_TransactionRolledBack_RemovesThePendingEntry()
    {
        // Arrange (Given)
        await using var database = await SqliteInMemoryDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync(
            TestContext.Current.CancellationToken);
        var storage = new EfCoreEventOutboxStorage<OutboxDbContext>(context);
        await storage.AddAsync(new OutboxTestEvent("rolled-back"), NoHeaders,
            TestContext.Current.CancellationToken);

        // Act (When)
        await transaction.RollbackAsync(TestContext.Current.CancellationToken);

        // Assert (Then) — a fresh context on the same database sees nothing: the row never committed
        await using var verifyContext = database.CreateContext();
        var count = await verifyContext.OutboxEvents.CountAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task AddAsync_UncommittedTransaction_IsInvisibleToAConcurrentConnection()
    {
        // Arrange (Given) — two separate connections to the same database, simulating two concurrent
        // sessions. This test uses a real temp-file SQLite database rather than
        // SqliteInMemoryDatabase's shared-cache ":memory:" connection: SQLite's shared-cache mode adds
        // cross-connection table-level locking that makes a second connection's read of a table with
        // an open, uncommitted writer transaction block (and, on a single sequential thread that never
        // gets to commit while the read is pending, eventually fail with "table is locked") instead of
        // returning the pre-transaction snapshot. A plain file-backed database gives each connection
        // its own page cache and reproduces normal SQLite MVCC-style behaviour: a reader sees the
        // last-committed snapshot without blocking, which is what this test needs to observe.
        var path = Path.Combine(Path.GetTempPath(), $"outbox-tx-tests-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={path}";
        try
        {
            await using (var setupContext =
                         new OutboxDbContext(new DbContextOptionsBuilder<OutboxDbContext>()
                             .UseSqlite(connectionString).Options))
            {
                await setupContext.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            }

            await using var writerContext =
                new OutboxDbContext(new DbContextOptionsBuilder<OutboxDbContext>()
                    .UseSqlite(connectionString).Options);
            await using var transaction = await writerContext.Database.BeginTransactionAsync(
                TestContext.Current.CancellationToken);
            var storage = new EfCoreEventOutboxStorage<OutboxDbContext>(writerContext);
            await storage.AddAsync(new OutboxTestEvent("in-flight"), NoHeaders,
                TestContext.Current.CancellationToken);

            // Act (When) — a second, independent connection reads before the writer commits
            await using var readerContext =
                new OutboxDbContext(new DbContextOptionsBuilder<OutboxDbContext>()
                    .UseSqlite(connectionString).Options);
            var countBeforeCommit = await readerContext.OutboxEvents.CountAsync(
                TestContext.Current.CancellationToken);

            await transaction.CommitAsync(TestContext.Current.CancellationToken);
            await using var readerContextAfterCommit =
                new OutboxDbContext(new DbContextOptionsBuilder<OutboxDbContext>()
                    .UseSqlite(connectionString).Options);
            var countAfterCommit = await readerContextAfterCommit.OutboxEvents.CountAsync(
                TestContext.Current.CancellationToken);

            // Assert (Then)
            Assert.Equal(0, countBeforeCommit);
            Assert.Equal(1, countAfterCommit);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [Fact]
    public async Task TwoDbContextsSharingOneTransaction_CommitTogether()
    {
        // Arrange (Given) — a business context and the outbox context share one DbTransaction, the
        // pattern the issue asked to be proven: Database.UseTransactionAsync. EF Core only allows a
        // DbTransaction to be reused by a context whose DbConnection is the very same object the
        // transaction was created on (see Microsoft's own "sharing transactions across contexts"
        // guidance), so both contexts are built against one explicitly shared SqliteConnection rather
        // than two contexts independently opened from the same connection string.
        await using var database = await SqliteInMemoryDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var sharedConnection = new SqliteConnection(database.ConnectionString);
        await sharedConnection.OpenAsync(TestContext.Current.CancellationToken);
        var businessOptions = new DbContextOptionsBuilder<BusinessDbContext>()
            .UseSqlite(sharedConnection)
            .Options;
        await using var businessContext = new BusinessDbContext(businessOptions);
        // EnsureCreatedAsync no-ops when the database already has any tables at all (it does
        // here — SqliteInMemoryDatabase already created the outbox table), so the
        // business_records table is created directly from the model's generated script instead.
        await businessContext.Database.ExecuteSqlRawAsync(businessContext.Database.GenerateCreateScript(),
            TestContext.Current.CancellationToken);
        var outboxOptions = new DbContextOptionsBuilder<OutboxDbContext>()
            .UseSqlite(sharedConnection)
            .Options;
        await using var outboxContext = new OutboxDbContext(outboxOptions);

        await using var transaction = await businessContext.Database.BeginTransactionAsync(
            TestContext.Current.CancellationToken);
        await outboxContext.Database.UseTransactionAsync(transaction.GetDbTransaction(),
            TestContext.Current.CancellationToken);

        var storage = new EfCoreEventOutboxStorage<OutboxDbContext>(outboxContext);
        var recordId = Guid.NewGuid();

        // Act (When)
        businessContext.Records.Add(new BusinessRecord { Id = recordId, Name = "widget" });
        await businessContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        await storage.AddAsync(new OutboxTestEvent("widget-created"), NoHeaders,
            TestContext.Current.CancellationToken);
        await transaction.CommitAsync(TestContext.Current.CancellationToken);

        // Assert (Then) — verify through fresh, independent connections: the writes are durable, not
        // artifacts of the shared connection still being open.
        await using var verifyBusiness = new BusinessDbContext(
            new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(database.ConnectionString).Options);
        await using var verifyOutbox = database.CreateContext();
        Assert.Equal(1, await verifyBusiness.Records.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await verifyOutbox.OutboxEvents.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TwoDbContextsSharingOneTransaction_RollBackTogether()
    {
        // Arrange (Given) — see TwoDbContextsSharingOneTransaction_CommitTogether for why both
        // contexts share one explicit SqliteConnection instead of two independently-opened ones.
        await using var database = await SqliteInMemoryDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var sharedConnection = new SqliteConnection(database.ConnectionString);
        await sharedConnection.OpenAsync(TestContext.Current.CancellationToken);
        var businessOptions = new DbContextOptionsBuilder<BusinessDbContext>()
            .UseSqlite(sharedConnection)
            .Options;
        await using var businessContext = new BusinessDbContext(businessOptions);
        // EnsureCreatedAsync no-ops when the database already has any tables at all (it does
        // here — SqliteInMemoryDatabase already created the outbox table), so the
        // business_records table is created directly from the model's generated script instead.
        await businessContext.Database.ExecuteSqlRawAsync(businessContext.Database.GenerateCreateScript(),
            TestContext.Current.CancellationToken);
        var outboxOptions = new DbContextOptionsBuilder<OutboxDbContext>()
            .UseSqlite(sharedConnection)
            .Options;
        await using var outboxContext = new OutboxDbContext(outboxOptions);

        await using var transaction = await businessContext.Database.BeginTransactionAsync(
            TestContext.Current.CancellationToken);
        await outboxContext.Database.UseTransactionAsync(transaction.GetDbTransaction(),
            TestContext.Current.CancellationToken);

        var storage = new EfCoreEventOutboxStorage<OutboxDbContext>(outboxContext);

        // Act (When)
        businessContext.Records.Add(new BusinessRecord { Id = Guid.NewGuid(), Name = "doomed-widget" });
        await businessContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        await storage.AddAsync(new OutboxTestEvent("doomed-widget-created"), NoHeaders,
            TestContext.Current.CancellationToken);
        await transaction.RollbackAsync(TestContext.Current.CancellationToken);

        // Assert (Then) — neither the business row nor the outbox row exist: they rolled back
        // together. Verify through fresh, independent connections.
        await using var verifyBusiness = new BusinessDbContext(
            new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(database.ConnectionString).Options);
        await using var verifyOutbox = database.CreateContext();
        Assert.Equal(0, await verifyBusiness.Records.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, await verifyOutbox.OutboxEvents.CountAsync(TestContext.Current.CancellationToken));
    }
}
