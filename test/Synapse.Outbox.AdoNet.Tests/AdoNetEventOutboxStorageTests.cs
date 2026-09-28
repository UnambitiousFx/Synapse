using System.Collections.Concurrent;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Outbox.AdoNet.Tests.Support;

namespace UnambitiousFx.Synapse.Outbox.AdoNet.Tests;

/// <summary>
///     Runs the same storage behavior against every supported engine; each subclass picks one.
/// </summary>
[TestSubject(typeof(AdoNetEventOutboxStorage))]
public abstract class AdoNetEventOutboxStorageTests
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    private static readonly IReadOnlyDictionary<string, string> NoHeaders =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private protected abstract DatabaseEngine Engine { get; }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AddAsync_ThenClaim_ReturnsTheEventWithItsHeadersCaseInsensitively()
    {
        // Arrange (Given)
        await using var database = await TestDatabase.CreateAsync(Engine, Token);
        var storage = database.CreateStorage();
        var headers = new Dictionary<string, string> { ["traceparent"] = "00-abc-def-01" };

        // Act (When)
        var added = await storage.AddAsync(new OutboxTestEvent("stored"), headers, Token);
        var claimed = await storage.ClaimPendingEventsAsync(null, Lease, Token);

        // Assert (Then)
        Assert.True(added.IsSuccess);
        var entry = Assert.Single(claimed);
        Assert.Equal("stored", Assert.IsType<OutboxTestEvent>(entry.Event).Name);
        Assert.Equal("00-abc-def-01", entry.Headers["TraceParent"]);
    }

    [Fact]
    public async Task ClaimPendingEventsAsync_ReturnsDueEntriesOldestFirst()
    {
        // Arrange (Given) — the middle entry is backing off after a failure
        await using var database = await TestDatabase.CreateAsync(Engine, Token);
        var storage = database.CreateStorage();
        foreach (var name in new[] { "first", "backing-off", "last" })
        {
            await storage.AddAsync(new OutboxTestEvent(name), NoHeaders, Token);
            await Task.Delay(5, Token);
        }

        var backingOff = (await storage.GetPendingEventsAsync(Token))[1];
        await storage.MarkAsFailedAsync(backingOff.Id, "transient", deadLetter: false,
            nextAttemptAt: DateTimeOffset.UtcNow.AddMinutes(10), cancellationToken: Token);

        // Act (When)
        var claimed = await storage.ClaimPendingEventsAsync(null, Lease, Token);

        // Assert (Then)
        Assert.Equal(["first", "last"], claimed.Select(e => ((OutboxTestEvent)e.Event).Name));
    }

    [Fact]
    public async Task ClaimPendingEventsAsync_WhileAnotherInstanceHoldsTheClaim_ReturnsNothing()
    {
        // Arrange (Given) — two storages on one table stand in for two application instances
        await using var database = await TestDatabase.CreateAsync(Engine, Token);
        var instanceA = database.CreateStorage();
        var instanceB = database.CreateStorage();
        await instanceA.AddAsync(new OutboxTestEvent("contended"), NoHeaders, Token);

        // Act (When)
        var claimedByA = await instanceA.ClaimPendingEventsAsync(null, Lease, Token);
        var claimedByB = await instanceB.ClaimPendingEventsAsync(null, Lease, Token);
        var pendingForB = await instanceB.GetPendingEventsAsync(Token);

        // Assert (Then) — still pending work, just not handed out twice
        Assert.Single(claimedByA);
        Assert.Empty(claimedByB);
        Assert.Empty(pendingForB);
        Assert.Equal(1, await instanceB.GetPendingCountAsync(Token));
    }

    [Fact]
    public async Task ClaimPendingEventsAsync_WithMaxCount_ClaimsAtMostThatMany()
    {
        // Arrange (Given)
        await using var database = await TestDatabase.CreateAsync(Engine, Token);
        var storage = database.CreateStorage();
        for (var i = 0; i < 3; i++)
        {
            await storage.AddAsync(new OutboxTestEvent($"event-{i}"), NoHeaders, Token);
        }

        // Act (When)
        var first = await storage.ClaimPendingEventsAsync(2, Lease, Token);
        var rest = await storage.ClaimPendingEventsAsync(2, Lease, Token);

        // Assert (Then)
        Assert.Equal(2, first.Count);
        Assert.Single(rest);
    }

    [Fact]
    public async Task ClaimPendingEventsAsync_AfterTheLeaseExpires_ClaimsTheEntryAgain()
    {
        // Arrange (Given) — a processor that claimed and then crashed never marks the entry
        await using var database = await TestDatabase.CreateAsync(Engine, Token);
        var storage = database.CreateStorage();
        await storage.AddAsync(new OutboxTestEvent("orphaned"), NoHeaders, Token);
        var crashed = await storage.ClaimPendingEventsAsync(null, TimeSpan.Zero, Token);
        await Task.Delay(5, Token);

        // Act (When)
        var reclaimed = await storage.ClaimPendingEventsAsync(null, Lease, Token);

        // Assert (Then)
        Assert.Equal(Assert.Single(crashed).Id, Assert.Single(reclaimed).Id);
    }

    [Fact]
    public async Task ClaimPendingEventsAsync_FromConcurrentInstances_HandsEachEntryOutOnce()
    {
        // Arrange (Given)
        await using var database = await TestDatabase.CreateAsync(Engine, Token);
        var seeder = database.CreateStorage();
        for (var i = 0; i < 200; i++)
        {
            await seeder.AddAsync(new OutboxTestEvent($"event-{i}"), NoHeaders, Token);
        }

        // Act (When) — each instance drains in small batches, all at once
        var instances = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            var storage = database.CreateStorage();
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
        }, Token));
        var claimedIds = (await Task.WhenAll(instances)).SelectMany(ids => ids).ToList();

        // Assert (Then)
        Assert.Equal(200, claimedIds.Count);
        Assert.Equal(200, claimedIds.Distinct().Count());
    }

    [Fact]
    public async Task MarkAsFailedAsync_NotDeadLettered_ReleasesTheClaimAndCountsTheAttempt()
    {
        // Arrange (Given)
        await using var database = await TestDatabase.CreateAsync(Engine, Token);
        var storage = database.CreateStorage();
        await storage.AddAsync(new OutboxTestEvent("retry-me"), NoHeaders, Token);
        var claimed = Assert.Single(await storage.ClaimPendingEventsAsync(null, Lease, Token));

        // Act (When)
        var result = await storage.MarkAsFailedAsync(claimed.Id, "transient", deadLetter: false,
            cancellationToken: Token);
        var retried = await storage.ClaimPendingEventsAsync(null, Lease, Token);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        Assert.Equal(claimed.Id, Assert.Single(retried).Id);
        Assert.Equal(1, await storage.GetAttemptCountAsync(claimed.Id, Token));
        Assert.Equal(1, await storage.GetRetryingCountAsync(Token));
    }

    [Fact]
    public async Task MarkAsFailedAsync_WithANonUtcNextAttempt_ComparesByInstant()
    {
        // Arrange (Given) — the wall-clock time at +14:00 reads as the future, but the instant has passed
        await using var database = await TestDatabase.CreateAsync(Engine, Token);
        var storage = database.CreateStorage();
        await storage.AddAsync(new OutboxTestEvent("due"), NoHeaders, Token);
        var claimed = Assert.Single(await storage.ClaimPendingEventsAsync(null, Lease, Token));
        var alreadyDue = DateTimeOffset.UtcNow.AddMinutes(-1).ToOffset(TimeSpan.FromHours(14));

        // Act (When)
        var result = await storage.MarkAsFailedAsync(claimed.Id, "transient", deadLetter: false,
            nextAttemptAt: alreadyDue, cancellationToken: Token);
        var retried = await storage.ClaimPendingEventsAsync(null, Lease, Token);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        Assert.Single(retried);
    }

    [Fact]
    public async Task MarkAsFailedAsync_DeadLettered_MovesTheEntryToTheDeadLetterQueue()
    {
        // Arrange (Given)
        await using var database = await TestDatabase.CreateAsync(Engine, Token);
        var storage = database.CreateStorage();
        await storage.AddAsync(new OutboxTestEvent("doomed"), NoHeaders, Token);
        var claimed = Assert.Single(await storage.ClaimPendingEventsAsync(null, Lease, Token));

        // Act (When)
        await storage.MarkAsFailedAsync(claimed.Id, "exhausted", deadLetter: true, cancellationToken: Token);

        // Assert (Then)
        Assert.Empty(await storage.ClaimPendingEventsAsync(null, TimeSpan.Zero, Token));
        Assert.Equal(claimed.Id, Assert.Single(await storage.GetDeadLetterEventsAsync(Token)).Id);
        Assert.Equal(1, await storage.GetDeadLetterCountAsync(Token));
        Assert.Equal(0, await storage.GetPendingCountAsync(Token));
    }

    [Fact]
    public async Task MarkAsProcessedAsync_RemovesTheEntryFromPending()
    {
        // Arrange (Given)
        await using var database = await TestDatabase.CreateAsync(Engine, Token);
        var storage = database.CreateStorage();
        await storage.AddAsync(new OutboxTestEvent("done"), NoHeaders, Token);
        var claimed = Assert.Single(await storage.ClaimPendingEventsAsync(null, Lease, Token));

        // Act (When)
        var result = await storage.MarkAsProcessedAsync(claimed.Id, Token);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        Assert.Equal(0, await storage.GetPendingCountAsync(Token));
        Assert.Null(await storage.GetOldestPendingAgeAsync(Token));
    }

    [Fact]
    public async Task MarkAsProcessedAsync_WithAnUnknownId_ReturnsFailure()
    {
        // Arrange (Given)
        await using var database = await TestDatabase.CreateAsync(Engine, Token);
        var storage = database.CreateStorage();

        // Act (When)
        var result = await storage.MarkAsProcessedAsync(Guid.NewGuid(), Token);

        // Assert (Then)
        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task GetOldestPendingAgeAsync_WithAPendingEntry_ReturnsItsAge()
    {
        // Arrange (Given)
        await using var database = await TestDatabase.CreateAsync(Engine, Token);
        var storage = database.CreateStorage();
        await storage.AddAsync(new OutboxTestEvent("aging"), NoHeaders, Token);

        // Act (When)
        var age = await storage.GetOldestPendingAgeAsync(Token);

        // Assert (Then)
        Assert.NotNull(age);
        Assert.InRange(age.Value, TimeSpan.FromSeconds(-1), TimeSpan.FromMinutes(1));
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public async Task AddAsync_InAnEnlistedTransaction_CommitsOrRollsBackWithIt(bool commit, int expectedPending)
    {
        // Arrange (Given)
        await using var database = await TestDatabase.CreateAsync(Engine, Token);
        var transactionHolder = new AdoNetOutboxTransaction();
        var storage = database.CreateStorage(transactionHolder);
        await using var connection = await database.DataSource.OpenConnectionAsync(Token);
        await using var transaction = await connection.BeginTransactionAsync(Token);
        transactionHolder.Enlist(transaction);

        // Act (When)
        var added = await storage.AddAsync(new OutboxTestEvent("transactional"), NoHeaders, Token);
        if (commit)
        {
            await transaction.CommitAsync(Token);
        }
        else
        {
            await transaction.RollbackAsync(Token);
        }

        // Assert (Then)
        Assert.True(added.IsSuccess);
        Assert.Equal(expectedPending, await storage.GetPendingCountAsync(Token));
    }

    [Fact]
    public async Task AddAsync_AfterTheEnlistedTransactionCompleted_ReturnsFailureAndStoresNothing()
    {
        // Arrange (Given)
        await using var database = await TestDatabase.CreateAsync(Engine, Token);
        var transactionHolder = new AdoNetOutboxTransaction();
        var storage = database.CreateStorage(transactionHolder);
        await using var connection = await database.DataSource.OpenConnectionAsync(Token);
        await using var transaction = await connection.BeginTransactionAsync(Token);
        transactionHolder.Enlist(transaction);
        await transaction.CommitAsync(Token);

        // Act (When)
        var added = await storage.AddAsync(new OutboxTestEvent("too-late"), NoHeaders, Token);

        // Assert (Then)
        Assert.True(added.IsFailure);
        Assert.Equal(0, await storage.GetPendingCountAsync(Token));
    }

    [Fact]
    public async Task DiscardAsync_WithAnEventThisInstanceStored_RemovesItFromPending()
    {
        // Arrange (Given)
        await using var database = await TestDatabase.CreateAsync(Engine, Token);
        var storage = database.CreateStorage();
        var discarded = new OutboxTestEvent("taken-back");
        await storage.AddAsync(discarded, NoHeaders, Token);
        await storage.AddAsync(new OutboxTestEvent("kept"), NoHeaders, Token);

        // Act (When)
        var result = await storage.DiscardAsync([discarded], Token);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        var remaining = Assert.Single(await storage.ClaimPendingEventsAsync(null, Lease, Token));
        Assert.Equal("kept", ((OutboxTestEvent)remaining.Event).Name);
    }

    [Fact]
    public async Task ClearAsync_RemovesEveryEntry()
    {
        // Arrange (Given)
        await using var database = await TestDatabase.CreateAsync(Engine, Token);
        var storage = database.CreateStorage();
        await storage.AddAsync(new OutboxTestEvent("cleared"), NoHeaders, Token);

        // Act (When)
        var result = await storage.ClearAsync(Token);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        Assert.Equal(0, Convert.ToInt32(await database.ScalarAsync($"SELECT COUNT(*) FROM {database.QualifiedTable}",
            Token)));
    }

    [Fact]
    public async Task AddSynapse_WithTheOutboxDispatcher_DispatchesStoredEvents()
    {
        // Arrange (Given) — the documented wiring: AddAdoNetEventOutbox before AddSynapse
        await using var database = await TestDatabase.CreateAsync(Engine, Token);
        var received = new ConcurrentQueue<string>();
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton(received);
        services.AddSingleton(database.DataSource);
        services.AddAdoNetEventOutbox(options =>
        {
            options.Dialect = database.Options.Dialect;
            options.Schema = database.Options.Schema;
            options.Table = database.Options.Table;
        });
        services.AddSynapse(cfg =>
        {
            cfg.SetEventOutboxStorage<AdoNetEventOutboxStorage>();
            cfg.RegisterEventHandler<RecordingHandler, OutboxTestEvent>();
            cfg.AddOutboxDispatcher(o => o.PollingInterval = TimeSpan.FromMilliseconds(20));
        });
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IEmitter>()
                .EmitAsync(new OutboxTestEvent("from-the-database"), EmitMode.Outbox, Token);
        }

        // Act (When)
        var hostedServices = provider.GetServices<IHostedService>().ToList();
        foreach (var service in hostedServices)
        {
            await service.StartAsync(Token);
        }

        // Waits for the entry to be marked processed, not just handled: stopping in between cancels the mark,
        // which leaves the entry for redelivery (at-least-once) rather than failing it.
        var storage = database.CreateStorage();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while ((received.IsEmpty || await storage.GetPendingCountAsync(Token) > 0) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20, Token);
        }

        foreach (var service in hostedServices)
        {
            await service.StopAsync(Token);
        }

        // Assert (Then)
        Assert.Equal(["from-the-database"], received);
        Assert.Equal(0, await storage.GetPendingCountAsync(Token));
    }

    private sealed class RecordingHandler(ConcurrentQueue<string> received) : IEventHandler<OutboxTestEvent>
    {
        public ValueTask<Result> HandleAsync(OutboxTestEvent @event, CancellationToken cancellationToken = default)
        {
            received.Enqueue(@event.Name);
            return new ValueTask<Result>(Result.Success());
        }
    }
}

public sealed class PostgreSqlEventOutboxStorageTests : AdoNetEventOutboxStorageTests
{
    private protected override DatabaseEngine Engine => DatabaseEngine.PostgreSql;
}

public sealed class SqlServerEventOutboxStorageTests : AdoNetEventOutboxStorageTests
{
    private protected override DatabaseEngine Engine => DatabaseEngine.SqlServer;
}
