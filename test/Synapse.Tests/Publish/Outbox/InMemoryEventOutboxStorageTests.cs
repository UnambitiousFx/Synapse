using JetBrains.Annotations;
using UnambitiousFx.Synapse.Contexts;
using UnambitiousFx.Synapse.Publish.Outbox;
using UnambitiousFx.Synapse.Tests.Definitions;

namespace UnambitiousFx.Synapse.Tests.Publish.Outbox;

[TestSubject(typeof(InMemoryEventOutboxStorage))]
public sealed class InMemoryEventOutboxStorageTests
{
    [Fact]
    public async Task MarkAsProcessedAsync_WithValueEqualEvents_UpdatesOnlyTargetInstance()
    {
        // Arrange (Given)
        var storage = new InMemoryEventOutboxStorage();
        var originalCorrelationId = CorrelationContext.CurrentCorrelationId;

        try
        {
            CorrelationContext.CurrentCorrelationId = Guid.NewGuid();
            var firstEvent = new EventExample("same-value");
            var secondEvent = new EventExample("same-value");

            await storage.AddAsync(firstEvent, CancellationToken.None);
            await storage.AddAsync(secondEvent, CancellationToken.None);

            // Act (When)
            var result = await storage.MarkAsProcessedAsync(firstEvent, CancellationToken.None);
            var pending = (await storage.GetPendingEventsAsync(CancellationToken.None)).ToList();

            // Assert (Then)
            Assert.True(result.IsSuccess);
            Assert.Single(pending);
            Assert.True(ReferenceEquals(secondEvent, pending[0]));
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
            await storage.AddAsync(firstEvent, CancellationToken.None);

            CorrelationContext.CurrentCorrelationId = Guid.NewGuid();
            var secondEvent = new EventExample("second");
            await storage.AddAsync(secondEvent, CancellationToken.None);

            CorrelationContext.CurrentCorrelationId = Guid.NewGuid();

            // Act (When)
            var pendingEvents = (await storage.GetPendingEventsAsync(CancellationToken.None)).ToList();

            // Assert (Then)
            Assert.Equal(2, pendingEvents.Count);
            Assert.Contains(pendingEvents, x => ReferenceEquals(x, firstEvent));
            Assert.Contains(pendingEvents, x => ReferenceEquals(x, secondEvent));
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
            await storage.AddAsync(new EventExample("first"), CancellationToken.None);

            CorrelationContext.CurrentCorrelationId = Guid.NewGuid();
            await storage.AddAsync(new EventExample("second"), CancellationToken.None);

            CorrelationContext.CurrentCorrelationId = Guid.NewGuid();

            // Act (When)
            var clearResult = await storage.ClearAsync(CancellationToken.None);
            var pendingCount = await storage.GetPendingCountAsync(CancellationToken.None);

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
    public async Task MarkAsProcessedAsync_WhenEventIsMissing_ReturnsFailure()
    {
        // Arrange (Given)
        var storage = new InMemoryEventOutboxStorage();

        // Act (When)
        var result = await storage.MarkAsProcessedAsync(new EventExample("missing"), CancellationToken.None);

        // Assert (Then)
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task MarkAsFailedAsync_WhenEventIsMissing_ReturnsFailure()
    {
        // Arrange (Given)
        var storage = new InMemoryEventOutboxStorage();

        // Act (When)
        var result = await storage.MarkAsFailedAsync(new EventExample("missing"), "error", false, null,
            CancellationToken.None);

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
            await storage.AddAsync(@event, CancellationToken.None);

            // Act (When)
            var failResult = await storage.MarkAsFailedAsync(@event, "boom", true, null, CancellationToken.None);
            var deadLetter = (await storage.GetDeadLetterEventsAsync(CancellationToken.None)).ToList();
            var pending = (await storage.GetPendingEventsAsync(CancellationToken.None)).ToList();
            var failedCount = await storage.GetFailedCountAsync(CancellationToken.None);

            // Assert (Then)
            Assert.True(failResult.IsSuccess);
            Assert.Single(deadLetter);
            Assert.Same(@event, deadLetter[0]);
            Assert.Empty(pending);
            Assert.Equal(0, failedCount);
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
            await storage.AddAsync(@event, CancellationToken.None);
            var nextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(10);

            // Act (When)
            await storage.MarkAsFailedAsync(@event, "temporary", false, nextAttemptAt, CancellationToken.None);
            var pending = (await storage.GetPendingEventsAsync(CancellationToken.None)).ToList();
            var attemptCount = await storage.GetAttemptCountAsync(@event, CancellationToken.None);
            var failedCount = await storage.GetFailedCountAsync(CancellationToken.None);

            // Assert (Then)
            Assert.Empty(pending);
            Assert.Equal(1, attemptCount);
            Assert.Equal(1, failedCount);
        }
        finally
        {
            CorrelationContext.CurrentCorrelationId = originalCorrelationId;
        }
    }

    [Fact]
    public async Task GetAttemptCountAsync_WhenEventMissing_ReturnsNull()
    {
        // Arrange (Given)
        var storage = new InMemoryEventOutboxStorage();

        // Act (When)
        var attempts = await storage.GetAttemptCountAsync(new EventExample("missing"), CancellationToken.None);

        // Assert (Then)
        Assert.Null(attempts);
    }

    [Fact]
    public async Task GetOldestPendingAgeAsync_WithNoPending_ReturnsNull()
    {
        // Arrange (Given)
        var storage = new InMemoryEventOutboxStorage();

        // Act (When)
        var age = await storage.GetOldestPendingAgeAsync(CancellationToken.None);

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
            await storage.AddAsync(new EventExample("oldest"), CancellationToken.None);

            // Act (When)
            var age = await storage.GetOldestPendingAgeAsync(CancellationToken.None);

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
