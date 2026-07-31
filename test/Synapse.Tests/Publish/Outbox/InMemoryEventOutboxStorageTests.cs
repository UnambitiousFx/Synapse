using JetBrains.Annotations;
using UnambitiousFx.Synapse.Publish.Outbox;
using UnambitiousFx.Synapse.Tests.Definitions;

namespace UnambitiousFx.Synapse.Tests.Publish.Outbox;

[TestSubject(typeof(InMemoryEventOutboxStorage))]
public sealed class InMemoryEventOutboxStorageTests
{
    private static readonly IReadOnlyDictionary<string, string> NoHeaders =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public async Task AddAsync_WithCapturedHeaders_SurfacesThemOnThePendingEntry()
    {
        // Arrange (Given) — the headers captured at store time are what let a later dispatch be traced back
        var storage = new InMemoryEventOutboxStorage();
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["baggage"] = "tenant.id=contoso",
            ["traceparent"] = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01"
        };

        // Act (When)
        await storage.AddAsync(new EventExample("with-headers"), headers,
            TestContext.Current.CancellationToken);
        var entries = (await storage.GetPendingEventsAsync(TestContext.Current.CancellationToken)).ToList();

        // Assert (Then)
        var entry = Assert.Single(entries);
        Assert.Equal(headers["baggage"], entry.Headers["baggage"]);
        Assert.Equal(headers["traceparent"], entry.Headers["traceparent"]);
    }

    [Fact]
    public async Task MarkAsProcessedAsync_WithDuplicateValueEqualEvents_MarksEachItemIndependently()
    {
        // Arrange (Given)
        var storage = new InMemoryEventOutboxStorage();
        await storage.AddAsync(new EventExample("same-value"), NoHeaders, TestContext.Current.CancellationToken);
        await storage.AddAsync(new EventExample("same-value"), NoHeaders, TestContext.Current.CancellationToken);

        var entries = (await storage.GetPendingEventsAsync(TestContext.Current.CancellationToken)).ToList();

        // Act (When)
        // Two value-equal events each get a distinct identity, so each can be marked on its own
        // without affecting the other.
        var firstResult = await storage.MarkAsProcessedAsync(entries[0].Id, TestContext.Current.CancellationToken);
        var afterFirst = (await storage.GetPendingEventsAsync(TestContext.Current.CancellationToken)).ToList();

        var secondResult = await storage.MarkAsProcessedAsync(entries[1].Id, TestContext.Current.CancellationToken);
        var afterSecond = (await storage.GetPendingEventsAsync(TestContext.Current.CancellationToken)).ToList();

        // Assert (Then)
        Assert.True(firstResult.IsSuccess);
        Assert.True(secondResult.IsSuccess);

        // Exactly one item remains pending after marking the first, and it is the *other* item.
        Assert.Single(afterFirst);
        Assert.Equal(entries[1].Id, afterFirst[0].Id);

        // Both items are now processed.
        Assert.Empty(afterSecond);
    }

    [Fact]
    public async Task MarkAsProcessedAsync_WithEntryId_FindsAndMarksItem()
    {
        // Arrange (Given)
        var storage = new InMemoryEventOutboxStorage();
        await storage.AddAsync(new EventExample("to-process"), NoHeaders, TestContext.Current.CancellationToken);
        var entry = (await storage.GetPendingEventsAsync(TestContext.Current.CancellationToken)).Single();

        // Act (When)
        var result = await storage.MarkAsProcessedAsync(entry.Id, TestContext.Current.CancellationToken);
        var pendingCount = await storage.GetPendingCountAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        Assert.Equal(0, pendingCount);
    }

    [Fact]
    public async Task GetPendingEventsAsync_WithEntriesFromDifferentFlows_ReturnsAllOfThemWithTheirOwnHeaders()
    {
        // Arrange (Given) — retrieval is deliberately not filtered by the flow that stored an entry, so one
        // processor drains everything; what keeps the flows apart is the headers travelling on each entry
        var storage = new InMemoryEventOutboxStorage();
        var firstEvent = new EventExample("first");
        var secondEvent = new EventExample("second");
        var firstHeaders = Headers("00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01");
        var secondHeaders = Headers("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01");

        await storage.AddAsync(firstEvent, firstHeaders, TestContext.Current.CancellationToken);
        await storage.AddAsync(secondEvent, secondHeaders, TestContext.Current.CancellationToken);

        // Act (When)
        var pendingEvents = (await storage.GetPendingEventsAsync(TestContext.Current.CancellationToken)).ToList();

        // Assert (Then)
        Assert.Equal(2, pendingEvents.Count);
        var first = Assert.Single(pendingEvents, x => ReferenceEquals(x.Event, firstEvent));
        var second = Assert.Single(pendingEvents, x => ReferenceEquals(x.Event, secondEvent));
        Assert.Equal(firstHeaders["traceparent"], first.Headers["traceparent"]);
        Assert.Equal(secondHeaders["traceparent"], second.Headers["traceparent"]);
    }

    [Fact]
    public async Task ClearAsync_WithEntriesFromDifferentFlows_ClearsAllOfThem()
    {
        // Arrange (Given)
        var storage = new InMemoryEventOutboxStorage();
        await storage.AddAsync(new EventExample("first"),
            Headers("00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01"),
            TestContext.Current.CancellationToken);
        await storage.AddAsync(new EventExample("second"),
            Headers("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"),
            TestContext.Current.CancellationToken);

        // Act (When)
        var clearResult = await storage.ClearAsync(TestContext.Current.CancellationToken);
        var pendingCount = await storage.GetPendingCountAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(clearResult.IsSuccess);
        Assert.Equal(0, pendingCount);
    }

    [Fact]
    public async Task MarkAsProcessedAsync_WhenItemIsMissing_ReturnsFailure()
    {
        // Arrange (Given)
        var storage = new InMemoryEventOutboxStorage();

        // Act (When)
        var result = await storage.MarkAsProcessedAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task MarkAsFailedAsync_WhenItemIsMissing_ReturnsFailure()
    {
        // Arrange (Given)
        var storage = new InMemoryEventOutboxStorage();

        // Act (When)
        var result = await storage.MarkAsFailedAsync(Guid.NewGuid(), "error", false, null,
            TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task MarkAsFailedAsync_WithDeadLetter_MovesEventToDeadLetterQueue()
    {
        // Arrange (Given)
        var storage = new InMemoryEventOutboxStorage();
        var @event = new EventExample("dead-letter");
        await storage.AddAsync(@event, NoHeaders, TestContext.Current.CancellationToken);
        var entry = (await storage.GetPendingEventsAsync(TestContext.Current.CancellationToken)).Single();

        // Act (When)
        var failResult = await storage.MarkAsFailedAsync(entry.Id, "boom", true, null,
            TestContext.Current.CancellationToken);
        var deadLetter = (await storage.GetDeadLetterEventsAsync(TestContext.Current.CancellationToken)).ToList();
        var pending = (await storage.GetPendingEventsAsync(TestContext.Current.CancellationToken)).ToList();
        var retryingCount = await storage.GetRetryingCountAsync(TestContext.Current.CancellationToken);
        var deadLetterCount = await storage.GetDeadLetterCountAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(failResult.IsSuccess);
        Assert.Single(deadLetter);
        Assert.Same(@event, deadLetter[0].Event);
        Assert.Empty(pending);
        // A dead-lettered event is not "retrying"; it counts only toward the dead-letter total.
        Assert.Equal(0, retryingCount);
        Assert.Equal(1, deadLetterCount);
    }

    [Fact]
    public async Task MarkAsFailedAsync_WithFutureRetry_ExcludesEventFromPendingUntilDue()
    {
        // Arrange (Given)
        var storage = new InMemoryEventOutboxStorage();
        await storage.AddAsync(new EventExample("retry-later"), NoHeaders, TestContext.Current.CancellationToken);
        var entry = (await storage.GetPendingEventsAsync(TestContext.Current.CancellationToken)).Single();
        var nextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(10);

        // Act (When)
        await storage.MarkAsFailedAsync(entry.Id, "temporary", false, nextAttemptAt,
            TestContext.Current.CancellationToken);
        var pending = (await storage.GetPendingEventsAsync(TestContext.Current.CancellationToken)).ToList();
        var attemptCount = await storage.GetAttemptCountAsync(entry.Id, TestContext.Current.CancellationToken);
        var retryingCount = await storage.GetRetryingCountAsync(TestContext.Current.CancellationToken);
        var deadLetterCount = await storage.GetDeadLetterCountAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.Empty(pending);
        Assert.Equal(1, attemptCount);
        // Scheduled for retry: counts as retrying, never as a dead-letter.
        Assert.Equal(1, retryingCount);
        Assert.Equal(0, deadLetterCount);
    }

    [Fact]
    public async Task GetAttemptCountAsync_WhenItemMissing_ReturnsNull()
    {
        // Arrange (Given)
        var storage = new InMemoryEventOutboxStorage();

        // Act (When)
        var attempts = await storage.GetAttemptCountAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.Null(attempts);
    }

    [Fact]
    public async Task GetOldestPendingAgeAsync_WithNoPending_ReturnsNull()
    {
        // Arrange (Given)
        var storage = new InMemoryEventOutboxStorage();

        // Act (When)
        var age = await storage.GetOldestPendingAgeAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.Null(age);
    }

    [Fact]
    public async Task GetOldestPendingAgeAsync_WithPending_ReturnsAge()
    {
        // Arrange (Given)
        var storage = new InMemoryEventOutboxStorage();
        await storage.AddAsync(new EventExample("oldest"), NoHeaders, TestContext.Current.CancellationToken);

        // Act (When)
        var age = await storage.GetOldestPendingAgeAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.NotNull(age);
        Assert.True(age.Value >= TimeSpan.Zero);
    }

    private static Dictionary<string, string> Headers(string traceparent)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["traceparent"] = traceparent
        };
    }
}
