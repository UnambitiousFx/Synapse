using JetBrains.Annotations;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Outbox.EntityFrameworkCore.Tests.Support;

namespace UnambitiousFx.Synapse.Outbox.EntityFrameworkCore.Tests;

[TestSubject(typeof(EfCoreEventOutboxStorage<OutboxDbContext>))]
public sealed class EfCoreEventOutboxStorageTests
{
    private static readonly IReadOnlyDictionary<string, string> NoHeaders =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public async Task AddAsync_ThenGetPendingEventsAsync_ReturnsTheStoredEvent()
    {
        // Arrange (Given)
        await using var database = await SqliteInMemoryDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var storage = new EfCoreEventOutboxStorage<OutboxDbContext>(context);

        // Act (When)
        var addResult = await storage.AddAsync(new OutboxTestEvent("stored"), NoHeaders,
            TestContext.Current.CancellationToken);
        var pending = await storage.GetPendingEventsAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(addResult.IsSuccess);
        var entry = Assert.Single(pending);
        var @event = Assert.IsType<OutboxTestEvent>(entry.Event);
        Assert.Equal("stored", @event.Name);
    }

    [Fact]
    public async Task AddAsync_WithHeaders_SurfacesThemOnThePendingEntry()
    {
        // Arrange (Given)
        await using var database = await SqliteInMemoryDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var storage = new EfCoreEventOutboxStorage<OutboxDbContext>(context);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["traceparent"] = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01"
        };

        // Act (When)
        await storage.AddAsync(new OutboxTestEvent("with-headers"), headers,
            TestContext.Current.CancellationToken);
        var pending = await storage.GetPendingEventsAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        var entry = Assert.Single(pending);
        Assert.Equal(headers["traceparent"], entry.Headers["traceparent"]);
    }

    [Fact]
    public async Task AddAsync_WithHeaders_SurfacesThemCaseInsensitivelyAfterARoundTrip()
    {
        // Arrange (Given) — System.Text.Json does not preserve a dictionary's IEqualityComparer
        // across serialize/deserialize, so the rehydrated header lookup must still be
        // case-insensitive like every other outbox header dictionary in this codebase.
        await using var database = await SqliteInMemoryDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var storage = new EfCoreEventOutboxStorage<OutboxDbContext>(context);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["TraceParent"] = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01"
        };

        // Act (When)
        await storage.AddAsync(new OutboxTestEvent("with-headers-cased"), headers,
            TestContext.Current.CancellationToken);
        var pending = await storage.GetPendingEventsAsync(TestContext.Current.CancellationToken);

        // Assert (Then) — looked up with a different casing than it was stored with
        var entry = Assert.Single(pending);
        Assert.Equal(headers["TraceParent"], entry.Headers["traceparent"]);
    }

    [Fact]
    public async Task MarkAsProcessedAsync_RemovesEntryFromPending()
    {
        // Arrange (Given)
        await using var database = await SqliteInMemoryDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var storage = new EfCoreEventOutboxStorage<OutboxDbContext>(context);
        await storage.AddAsync(new OutboxTestEvent("to-process"), NoHeaders,
            TestContext.Current.CancellationToken);
        var stored = Assert.Single(
            await storage.GetPendingEventsAsync(TestContext.Current.CancellationToken));

        // Act (When)
        var result = await storage.MarkAsProcessedAsync(stored.Id, TestContext.Current.CancellationToken);
        var pendingAfter = await storage.GetPendingEventsAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        Assert.Empty(pendingAfter);
    }

    [Fact]
    public async Task MarkAsProcessedAsync_WithUnknownId_ReturnsFailure()
    {
        // Arrange (Given)
        await using var database = await SqliteInMemoryDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var storage = new EfCoreEventOutboxStorage<OutboxDbContext>(context);

        // Act (When)
        var result = await storage.MarkAsProcessedAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task MarkAsFailedAsync_NotDeadLetter_SchedulesNextAttemptAndIncrementsAttempts()
    {
        // Arrange (Given)
        await using var database = await SqliteInMemoryDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var storage = new EfCoreEventOutboxStorage<OutboxDbContext>(context);
        await storage.AddAsync(new OutboxTestEvent("retry-me"), NoHeaders,
            TestContext.Current.CancellationToken);
        var stored = Assert.Single(
            await storage.GetPendingEventsAsync(TestContext.Current.CancellationToken));
        var nextAttempt = DateTimeOffset.UtcNow.AddMinutes(5);

        // Act (When)
        var result = await storage.MarkAsFailedAsync(stored.Id, "transient failure", deadLetter: false,
            nextAttemptAt: nextAttempt, cancellationToken: TestContext.Current.CancellationToken);
        var attempts = await storage.GetAttemptCountAsync(stored.Id, TestContext.Current.CancellationToken);
        var retrying = await storage.GetRetryingCountAsync(TestContext.Current.CancellationToken);
        var pending = await storage.GetPendingEventsAsync(TestContext.Current.CancellationToken);

        // Assert (Then) — it is still counted as retrying, but is withheld from dispatch until its
        // scheduled NextAttemptAt passes.
        Assert.True(result.IsSuccess);
        Assert.Equal(1, attempts);
        Assert.Equal(1, retrying);
        Assert.Empty(pending);
    }

    [Fact]
    public async Task GetPendingEventsAsync_WithNextAttemptAtInThePast_ReturnsTheEntryAgain()
    {
        // Arrange (Given) — the companion of the back-off case above: once the scheduled retry time
        // has passed, the entry becomes dispatchable again.
        await using var database = await SqliteInMemoryDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var storage = new EfCoreEventOutboxStorage<OutboxDbContext>(context);
        await storage.AddAsync(new OutboxTestEvent("retry-due"), NoHeaders,
            TestContext.Current.CancellationToken);
        var stored = Assert.Single(
            await storage.GetPendingEventsAsync(TestContext.Current.CancellationToken));

        // Act (When)
        await storage.MarkAsFailedAsync(stored.Id, "transient failure", deadLetter: false,
            nextAttemptAt: DateTimeOffset.UtcNow.AddMinutes(-1),
            cancellationToken: TestContext.Current.CancellationToken);
        var pending = await storage.GetPendingEventsAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        var entry = Assert.Single(pending);
        Assert.Equal(stored.Id, entry.Id);
    }

    [Fact]
    public async Task MarkAsFailedAsync_DeadLetter_MovesEntryToDeadLetterQueue()
    {
        // Arrange (Given)
        await using var database = await SqliteInMemoryDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var storage = new EfCoreEventOutboxStorage<OutboxDbContext>(context);
        await storage.AddAsync(new OutboxTestEvent("doomed"), NoHeaders,
            TestContext.Current.CancellationToken);
        var stored = Assert.Single(
            await storage.GetPendingEventsAsync(TestContext.Current.CancellationToken));

        // Act (When)
        await storage.MarkAsFailedAsync(stored.Id, "exhausted retries", deadLetter: true,
            cancellationToken: TestContext.Current.CancellationToken);
        var pending = await storage.GetPendingEventsAsync(TestContext.Current.CancellationToken);
        var deadLetter = await storage.GetDeadLetterEventsAsync(TestContext.Current.CancellationToken);
        var deadLetterCount = await storage.GetDeadLetterCountAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.Empty(pending);
        Assert.Single(deadLetter);
        Assert.Equal(1, deadLetterCount);
    }

    [Fact]
    public async Task GetOldestPendingAgeAsync_WithNoPendingEntries_ReturnsNull()
    {
        // Arrange (Given)
        await using var database = await SqliteInMemoryDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var storage = new EfCoreEventOutboxStorage<OutboxDbContext>(context);

        // Act (When)
        var age = await storage.GetOldestPendingAgeAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.Null(age);
    }

    [Fact]
    public async Task GetOldestPendingAgeAsync_WithPendingEntry_ReturnsNonNegativeAge()
    {
        // Arrange (Given)
        await using var database = await SqliteInMemoryDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var storage = new EfCoreEventOutboxStorage<OutboxDbContext>(context);
        await storage.AddAsync(new OutboxTestEvent("aging"), NoHeaders,
            TestContext.Current.CancellationToken);

        // Act (When)
        var age = await storage.GetOldestPendingAgeAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.NotNull(age);
        Assert.True(age.Value >= TimeSpan.Zero);
    }

    [Fact]
    public async Task ClearAsync_RemovesEverything()
    {
        // Arrange (Given)
        await using var database = await SqliteInMemoryDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var storage = new EfCoreEventOutboxStorage<OutboxDbContext>(context);
        await storage.AddAsync(new OutboxTestEvent("one"), NoHeaders, TestContext.Current.CancellationToken);
        await storage.AddAsync(new OutboxTestEvent("two"), NoHeaders, TestContext.Current.CancellationToken);

        // Act (When)
        var result = await storage.ClearAsync(TestContext.Current.CancellationToken);
        var pending = await storage.GetPendingEventsAsync(TestContext.Current.CancellationToken);
        var count = await storage.GetPendingCountAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        Assert.Empty(pending);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task DiscardAsync_MatchingPendingEvent_RemovesItFromPending()
    {
        // Arrange (Given)
        await using var database = await SqliteInMemoryDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var storage = new EfCoreEventOutboxStorage<OutboxDbContext>(context);
        var @event = new OutboxTestEvent("to-discard");
        await storage.AddAsync(@event, NoHeaders, TestContext.Current.CancellationToken);

        // Act (When)
        var result = await storage.DiscardAsync([@event], TestContext.Current.CancellationToken);
        var pending = await storage.GetPendingEventsAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        Assert.Empty(pending);
    }

    [Fact]
    public async Task DiscardAsync_SameEventReferenceStoredTwice_DiscardsBothRows()
    {
        // Arrange (Given) — the same event instance added twice produces two rows, and a discard must
        // take both back, matching InMemoryEventOutboxStorage which discards every stored item whose
        // event reference matches.
        await using var database = await SqliteInMemoryDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var storage = new EfCoreEventOutboxStorage<OutboxDbContext>(context);
        var @event = new OutboxTestEvent("stored-twice");
        await storage.AddAsync(@event, NoHeaders, TestContext.Current.CancellationToken);
        await storage.AddAsync(@event, NoHeaders, TestContext.Current.CancellationToken);
        Assert.Equal(2, await storage.GetPendingCountAsync(TestContext.Current.CancellationToken));

        // Act (When)
        var result = await storage.DiscardAsync([@event], TestContext.Current.CancellationToken);
        var pending = await storage.GetPendingEventsAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        Assert.Empty(pending);
    }

    [Fact]
    public async Task DiscardAsync_EventThisInstanceNeverStored_IsIgnored()
    {
        // Arrange (Given) — matched by reference: an event this storage instance never added is not
        // a match for any row, even if a value-equal one exists.
        await using var database = await SqliteInMemoryDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var storage = new EfCoreEventOutboxStorage<OutboxDbContext>(context);
        await storage.AddAsync(new OutboxTestEvent("kept"), NoHeaders, TestContext.Current.CancellationToken);
        var unrelatedEvent = new OutboxTestEvent("kept"); // value-equal, different reference

        // Act (When)
        var result = await storage.DiscardAsync([unrelatedEvent], TestContext.Current.CancellationToken);
        var pending = await storage.GetPendingEventsAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        Assert.Single(pending);
    }

    [Fact]
    public async Task DiscardAsync_AlreadyProcessedEvent_IsUntouched()
    {
        // Arrange (Given)
        await using var database = await SqliteInMemoryDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        var storage = new EfCoreEventOutboxStorage<OutboxDbContext>(context);
        var @event = new OutboxTestEvent("already-done");
        await storage.AddAsync(@event, NoHeaders, TestContext.Current.CancellationToken);
        var stored = Assert.Single(
            await storage.GetPendingEventsAsync(TestContext.Current.CancellationToken));
        await storage.MarkAsProcessedAsync(stored.Id, TestContext.Current.CancellationToken);

        // Act (When)
        var result = await storage.DiscardAsync([@event], TestContext.Current.CancellationToken);
        var attempts = await storage.GetAttemptCountAsync(stored.Id, TestContext.Current.CancellationToken);

        // Assert (Then) — the row still exists and is unaffected (not re-queryable via pending, but
        // GetAttemptCountAsync finds it by id regardless of status, proving it was not deleted/altered)
        Assert.True(result.IsSuccess);
        Assert.Equal(0, attempts);
    }
}
