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
}
