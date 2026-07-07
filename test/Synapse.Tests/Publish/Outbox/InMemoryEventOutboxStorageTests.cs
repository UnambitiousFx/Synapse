using JetBrains.Annotations;
using UnambitiousFx.Synapse.Contexts;
using UnambitiousFx.Synapse.Publish.Outbox;
using UnambitiousFx.Synapse.Tests.Definitions;

namespace UnambitiousFx.Synapse.Tests.Publish.Outbox;

[TestSubject(typeof(InMemoryEventOutboxStorage))]
public sealed class InMemoryEventOutboxStorageTests
{
    [Fact]
    public async Task MarkAsProcessedAsync_WithDuplicateValueEqualEvents_MarksEachItemIndependently()
    {
        // Arrange (Given)
        var storage = new InMemoryEventOutboxStorage();
        var originalCorrelationId = CorrelationContext.CurrentCorrelationId;

        try
        {
            CorrelationContext.CurrentCorrelationId = Guid.NewGuid();
            await storage.AddAsync(new EventExample("same-value"), TestContext.Current.CancellationToken);
            await storage.AddAsync(new EventExample("same-value"), TestContext.Current.CancellationToken);

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
        finally
        {
            CorrelationContext.CurrentCorrelationId = originalCorrelationId;
        }
    }

    [Fact]
    public async Task MarkAsProcessedAsync_WithEntryId_FindsAndMarksItem()
    {
        // Arrange (Given)
        var storage = new InMemoryEventOutboxStorage();
        var originalCorrelationId = CorrelationContext.CurrentCorrelationId;

        try
        {
            CorrelationContext.CurrentCorrelationId = Guid.NewGuid();
            await storage.AddAsync(new EventExample("to-process"), TestContext.Current.CancellationToken);
            var entry = (await storage.GetPendingEventsAsync(TestContext.Current.CancellationToken)).Single();

            // Act (When)
            var result = await storage.MarkAsProcessedAsync(entry.Id, TestContext.Current.CancellationToken);
            var pendingCount = await storage.GetPendingCountAsync(TestContext.Current.CancellationToken);

            // Assert (Then)
            Assert.True(result.IsSuccess);
            Assert.Equal(0, pendingCount);
        }
        finally
        {
            CorrelationContext.CurrentCorrelationId = originalCorrelationId;
        }
    }

    [Fact]
    public async Task GetPendingEventsAsync_WhenEventsAddedInDifferentCorrelations_ReturnsAllPendingEvents()
    {
        // Arrange (Given)
        var storage = new InMemoryEventOutboxStorage();
        var originalCorrelationId = CorrelationContext.CurrentCorrelationId;

        try
        {
            CorrelationContext.CurrentCorrelationId = Guid.NewGuid();
            var firstEvent = new EventExample("first");
            await storage.AddAsync(firstEvent, TestContext.Current.CancellationToken);

            CorrelationContext.CurrentCorrelationId = Guid.NewGuid();
            var secondEvent = new EventExample("second");
            await storage.AddAsync(secondEvent, TestContext.Current.CancellationToken);

            CorrelationContext.CurrentCorrelationId = Guid.NewGuid();

            // Act (When)
            var pendingEvents = (await storage.GetPendingEventsAsync(TestContext.Current.CancellationToken)).ToList();

            // Assert (Then)
            Assert.Equal(2, pendingEvents.Count);
            Assert.Contains(pendingEvents, x => ReferenceEquals(x.Event, firstEvent));
            Assert.Contains(pendingEvents, x => ReferenceEquals(x.Event, secondEvent));
        }
        finally
        {
            CorrelationContext.CurrentCorrelationId = originalCorrelationId;
        }
    }

    [Fact]
    public async Task ClearAsync_WhenEventsExistAcrossCorrelations_ClearsAllEvents()
    {
        // Arrange (Given)
        var storage = new InMemoryEventOutboxStorage();
        var originalCorrelationId = CorrelationContext.CurrentCorrelationId;

        try
        {
            CorrelationContext.CurrentCorrelationId = Guid.NewGuid();
            await storage.AddAsync(new EventExample("first"), TestContext.Current.CancellationToken);

            CorrelationContext.CurrentCorrelationId = Guid.NewGuid();
            await storage.AddAsync(new EventExample("second"), TestContext.Current.CancellationToken);

            CorrelationContext.CurrentCorrelationId = Guid.NewGuid();

            // Act (When)
            var clearResult = await storage.ClearAsync(TestContext.Current.CancellationToken);
            var pendingCount = await storage.GetPendingCountAsync(TestContext.Current.CancellationToken);

            // Assert (Then)
            Assert.True(clearResult.IsSuccess);
            Assert.Equal(0, pendingCount);
        }
        finally
        {
            CorrelationContext.CurrentCorrelationId = originalCorrelationId;
        }
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
        var originalCorrelationId = CorrelationContext.CurrentCorrelationId;

        try
        {
            CorrelationContext.CurrentCorrelationId = Guid.NewGuid();
            var @event = new EventExample("dead-letter");
            await storage.AddAsync(@event, TestContext.Current.CancellationToken);
            var entry = (await storage.GetPendingEventsAsync(TestContext.Current.CancellationToken)).Single();

            // Act (When)
            var failResult = await storage.MarkAsFailedAsync(entry.Id, "boom", true, null, TestContext.Current.CancellationToken);
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
        finally
        {
            CorrelationContext.CurrentCorrelationId = originalCorrelationId;
        }
    }

    [Fact]
    public async Task MarkAsFailedAsync_WithFutureRetry_ExcludesEventFromPendingUntilDue()
    {
        // Arrange (Given)
        var storage = new InMemoryEventOutboxStorage();
        var originalCorrelationId = CorrelationContext.CurrentCorrelationId;

        try
        {
            CorrelationContext.CurrentCorrelationId = Guid.NewGuid();
            var @event = new EventExample("retry-later");
            await storage.AddAsync(@event, TestContext.Current.CancellationToken);
            var entry = (await storage.GetPendingEventsAsync(TestContext.Current.CancellationToken)).Single();
            var nextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(10);

            // Act (When)
            await storage.MarkAsFailedAsync(entry.Id, "temporary", false, nextAttemptAt, TestContext.Current.CancellationToken);
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
        finally
        {
            CorrelationContext.CurrentCorrelationId = originalCorrelationId;
        }
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
        var originalCorrelationId = CorrelationContext.CurrentCorrelationId;

        try
        {
            CorrelationContext.CurrentCorrelationId = Guid.NewGuid();
            await storage.AddAsync(new EventExample("oldest"), TestContext.Current.CancellationToken);

            // Act (When)
            var age = await storage.GetOldestPendingAgeAsync(TestContext.Current.CancellationToken);

            // Assert (Then)
            Assert.NotNull(age);
            Assert.True(age.Value >= TimeSpan.Zero);
        }
        finally
        {
            CorrelationContext.CurrentCorrelationId = originalCorrelationId;
        }
    }
}
