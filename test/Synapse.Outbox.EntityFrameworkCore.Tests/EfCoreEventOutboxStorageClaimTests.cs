using System.Text.Json;
using JetBrains.Annotations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Outbox.EntityFrameworkCore.Tests.Support;

namespace UnambitiousFx.Synapse.Outbox.EntityFrameworkCore.Tests;

[TestSubject(typeof(EfCoreEventOutboxStorage<OutboxDbContext>))]
public sealed class EfCoreEventOutboxStorageClaimTests
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    private static readonly IReadOnlyDictionary<string, string> NoHeaders =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public async Task ClaimPendingEventsAsync_ReturnsDueEntriesOldestFirst()
    {
        // Arrange (Given)
        await using var database = await SqliteInMemoryDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var storage = new EfCoreEventOutboxStorage<OutboxDbContext>(context);
        var now = DateTimeOffset.UtcNow;
        context.OutboxEvents.AddRange(
            CreateRow("newest", now.AddMinutes(-1)),
            CreateRow("oldest", now.AddHours(-1)),
            CreateRow("backing-off", now.AddHours(-2), nextAttemptAt: now.AddMinutes(10)));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act (When)
        var claimed = await storage.ClaimPendingEventsAsync(null, Lease, TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.Equal(["oldest", "newest"], claimed.Select(e => Assert.IsType<OutboxTestEvent>(e.Event).Name));
    }

    [Fact]
    public async Task ClaimPendingEventsAsync_WhileAnotherProcessHoldsTheClaim_ReturnsNothing()
    {
        // Arrange (Given) — two contexts on one database stand in for two application instances
        await using var database = await SqliteInMemoryDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var instanceA = database.CreateContext();
        await using var instanceB = database.CreateContext();
        var storageA = new EfCoreEventOutboxStorage<OutboxDbContext>(instanceA);
        var storageB = new EfCoreEventOutboxStorage<OutboxDbContext>(instanceB);
        await storageA.AddAsync(new OutboxTestEvent("contended"), NoHeaders, TestContext.Current.CancellationToken);

        // Act (When)
        var claimedByA = await storageA.ClaimPendingEventsAsync(null, Lease, TestContext.Current.CancellationToken);
        var claimedByB = await storageB.ClaimPendingEventsAsync(null, Lease, TestContext.Current.CancellationToken);
        var pendingForB = await storageB.GetPendingEventsAsync(TestContext.Current.CancellationToken);

        // Assert (Then) — claimed rows are still pending work, just not handed out twice
        Assert.Single(claimedByA);
        Assert.Empty(claimedByB);
        Assert.Empty(pendingForB);
        Assert.Equal(1, await storageB.GetPendingCountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ClaimPendingEventsAsync_WithMaxCount_ClaimsAtMostThatMany()
    {
        // Arrange (Given)
        await using var database = await SqliteInMemoryDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var storage = new EfCoreEventOutboxStorage<OutboxDbContext>(context);
        for (var i = 0; i < 3; i++)
        {
            await storage.AddAsync(new OutboxTestEvent($"event-{i}"), NoHeaders,
                TestContext.Current.CancellationToken);
        }

        // Act (When)
        var first = await storage.ClaimPendingEventsAsync(2, Lease, TestContext.Current.CancellationToken);
        var rest = await storage.ClaimPendingEventsAsync(2, Lease, TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.Equal(2, first.Count);
        Assert.Single(rest);
    }

    [Fact]
    public async Task ClaimPendingEventsAsync_AfterTheLeaseExpires_ClaimsTheEntryAgain()
    {
        // Arrange (Given) — a processor that claimed and then crashed never marks the entry
        await using var database = await SqliteInMemoryDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var storage = new EfCoreEventOutboxStorage<OutboxDbContext>(context);
        await storage.AddAsync(new OutboxTestEvent("orphaned"), NoHeaders, TestContext.Current.CancellationToken);
        var crashed = await storage.ClaimPendingEventsAsync(null, TimeSpan.Zero,
            TestContext.Current.CancellationToken);

        // Act (When)
        var reclaimed = await storage.ClaimPendingEventsAsync(null, Lease, TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.Equal(Assert.Single(crashed).Id, Assert.Single(reclaimed).Id);
    }

    [Fact]
    public async Task MarkAsFailedAsync_InTheContextThatStoredTheEntry_ReleasesTheClaimForTheRetry()
    {
        // Arrange (Given) — store, claim and fail through one context, as a commit in the storing scope does;
        // that context still tracks the row from AddAsync, while the claim was written behind its back
        await using var database = await SqliteInMemoryDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var storage = new EfCoreEventOutboxStorage<OutboxDbContext>(context);
        await storage.AddAsync(new OutboxTestEvent("retry-me"), NoHeaders, TestContext.Current.CancellationToken);
        var claimed = Assert.Single(await storage.ClaimPendingEventsAsync(null, Lease,
            TestContext.Current.CancellationToken));

        // Act (When)
        await storage.MarkAsFailedAsync(claimed.Id, "transient", deadLetter: false,
            cancellationToken: TestContext.Current.CancellationToken);
        await using var otherInstance = database.CreateContext();
        var retried = await new EfCoreEventOutboxStorage<OutboxDbContext>(otherInstance)
            .ClaimPendingEventsAsync(null, Lease, TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.Equal(claimed.Id, Assert.Single(retried).Id);
    }

    [Fact]
    public async Task MarkAsProcessedAsync_OnAClaimedEntry_ClearsTheClaimColumns()
    {
        // Arrange (Given)
        await using var database = await SqliteInMemoryDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var storage = new EfCoreEventOutboxStorage<OutboxDbContext>(context);
        await storage.AddAsync(new OutboxTestEvent("done"), NoHeaders, TestContext.Current.CancellationToken);
        var claimed = Assert.Single(await storage.ClaimPendingEventsAsync(null, Lease,
            TestContext.Current.CancellationToken));

        // Act (When)
        await storage.MarkAsProcessedAsync(claimed.Id, TestContext.Current.CancellationToken);

        // Assert (Then)
        await using var reader = database.CreateContext();
        var row = await reader.OutboxEvents.SingleAsync(e => e.Id == claimed.Id,
            TestContext.Current.CancellationToken);
        Assert.True(row.Processed);
        Assert.Null(row.ClaimedUntil);
        Assert.Null(row.ClaimToken);
    }

    [Fact]
    public async Task ClaimPendingEventsAsync_FromConcurrentInstances_HandsEachEntryOutOnce()
    {
        // Arrange (Given) — a file database, so each instance has its own connection and the database itself
        // arbitrates between them
        var path = Path.Combine(Path.GetTempPath(), $"outbox-claim-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={path};Pooling=False";
        try
        {
            await using (var seed = CreateFileContext(connectionString))
            {
                await seed.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
                var now = DateTimeOffset.UtcNow;
                seed.OutboxEvents.AddRange(Enumerable.Range(0, 200)
                    .Select(i => CreateRow($"event-{i}", now.AddSeconds(-i))));
                await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
            }

            // Act (When)
            var instances = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
            {
                await using var context = CreateFileContext(connectionString);
                var storage = new EfCoreEventOutboxStorage<OutboxDbContext>(context);
                var ids = new List<Guid>();
                while (true)
                {
                    var batch = await storage.ClaimPendingEventsAsync(7, Lease);
                    if (batch.Count == 0)
                    {
                        return ids;
                    }

                    ids.AddRange(batch.Select(e => e.Id));
                }
            }, TestContext.Current.CancellationToken));
            var claimedIds = (await Task.WhenAll(instances)).SelectMany(ids => ids).ToList();

            // Assert (Then)
            Assert.Equal(200, claimedIds.Count);
            Assert.Equal(200, claimedIds.Distinct().Count());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    private static OutboxDbContext CreateFileContext(string connectionString)
    {
        return new OutboxDbContext(new DbContextOptionsBuilder<OutboxDbContext>()
            .UseSqlite(connectionString)
            .Options);
    }

    private static OutboxEntity CreateRow(string name,
        DateTimeOffset createdAt,
        DateTimeOffset? nextAttemptAt = null)
    {
        return new OutboxEntity
        {
            Id = Guid.NewGuid(),
            EventType = typeof(OutboxTestEvent).AssemblyQualifiedName!,
            Payload = JsonSerializer.Serialize(new OutboxTestEvent(name)),
            Headers = "{}",
            CreatedAt = createdAt,
            NextAttemptAt = nextAttemptAt
        };
    }
}
