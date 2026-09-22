using JetBrains.Annotations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UnambitiousFx.Synapse.Outbox.EntityFrameworkCore.Tests.Support;

namespace UnambitiousFx.Synapse.Outbox.EntityFrameworkCore.Tests;

[TestSubject(typeof(OutboxEntityTypeConfiguration))]
public sealed class OutboxEntityTypeConfigurationTests
{
    private static readonly IReadOnlyDictionary<string, string> NoHeaders =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public async Task AppliedToTheStandaloneOutboxDbContext_PersistsAndReadsBack()
    {
        // Arrange (Given)
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var options = new DbContextOptionsBuilder<OutboxDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new OutboxDbContext(options);
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        var entity = new OutboxEntity
        {
            Id = Guid.NewGuid(),
            EventType = "System.String",
            Payload = "\"hello\"",
            Headers = "{}",
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Act (When)
        context.OutboxEvents.Add(entity);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        var reloaded = await context.OutboxEvents
            .AsNoTracking()
            .SingleAsync(e => e.Id == entity.Id, TestContext.Current.CancellationToken);
        Assert.Equal(entity.Payload, reloaded.Payload);
    }

    [Fact]
    public async Task AppliedInsideACallerOwnedDbContext_MapsOutboxTableAlongsideTheCallersOwnEntities()
    {
        // Arrange (Given) — the "embed the table in your own DbContext" path from the docs: one
        // caller-owned context whose OnModelCreating applies OutboxEntityTypeConfiguration next to an
        // unrelated entity of its own. EnsureCreated must produce BOTH tables from that single model.
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var options = new DbContextOptionsBuilder<EmbeddedOutboxDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new EmbeddedOutboxDbContext(options);
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        var recordId = Guid.NewGuid();
        var outboxId = Guid.NewGuid();

        // Act (When) — a business row and an outbox row written through the one context instance
        context.Records.Add(new BusinessRecord { Id = recordId, Name = "widget" });
        context.OutboxEvents.Add(new OutboxEntity
        {
            Id = outboxId,
            EventType = "System.String",
            Payload = "\"embedded\"",
            Headers = "{}",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert (Then) — both read back from that same context
        var reloadedRecord = await context.Records
            .AsNoTracking()
            .SingleAsync(r => r.Id == recordId, TestContext.Current.CancellationToken);
        var reloadedOutbox = await context.OutboxEvents
            .AsNoTracking()
            .SingleAsync(e => e.Id == outboxId, TestContext.Current.CancellationToken);

        Assert.Equal("widget", reloadedRecord.Name);
        Assert.Equal("\"embedded\"", reloadedOutbox.Payload);
    }

    [Fact]
    public async Task EfCoreEventOutboxStorage_OverACallerOwnedDbContext_StoresAndReadsPendingEvents()
    {
        // Arrange (Given) — the storage itself resolves the outbox table via Set<OutboxEntity>(), so
        // it must work against a caller-owned context that merely applied the configuration, not only
        // against the standalone OutboxDbContext.
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var options = new DbContextOptionsBuilder<EmbeddedOutboxDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new EmbeddedOutboxDbContext(options);
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var storage = new EfCoreEventOutboxStorage<EmbeddedOutboxDbContext>(context);

        // Act (When) — a business write and an outbox write through the same context and transaction
        context.Records.Add(new BusinessRecord { Id = Guid.NewGuid(), Name = "embedded-widget" });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var addResult = await storage.AddAsync(new OutboxTestEvent("embedded-widget-created"), NoHeaders,
            TestContext.Current.CancellationToken);
        var pending = await storage.GetPendingEventsAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(addResult.IsSuccess);
        var entry = Assert.Single(pending);
        var @event = Assert.IsType<OutboxTestEvent>(entry.Event);
        Assert.Equal("embedded-widget-created", @event.Name);
        Assert.Equal(1, await context.Records.CountAsync(TestContext.Current.CancellationToken));
    }
}
