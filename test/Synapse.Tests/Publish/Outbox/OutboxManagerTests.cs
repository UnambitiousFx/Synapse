using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Observability;
using UnambitiousFx.Synapse.Publish;
using UnambitiousFx.Synapse.Publish.Outbox;
using UnambitiousFx.Synapse.Tests.Definitions;

namespace UnambitiousFx.Synapse.Tests.Publish.Outbox;

[TestSubject(typeof(OutboxManager))]
public sealed class OutboxManagerTests
{
    [Fact]
    public async Task ProcessPendingAsync_WhenCanceled_DoesNotMarkEventAsFailed()
    {
        // Arrange (Given)
        var outboxStorage = Substitute.For<IEventOutboxStorage>();
        var eventDispatcher = Substitute.For<IEventDispatcher>();
        var metrics = Substitute.For<ISynapseMetrics>();
        var logger = Substitute.For<ILogger<OutboxManager>>();
        var @event = new EventExample("event-1");

        outboxStorage.GetPendingEventsAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<OutboxEntry>>(
                new[] { new OutboxEntry(Guid.NewGuid(), @event) }));

        var options = new EventDispatcherOptions
        {
            Dispatchers = new Dictionary<Type, DispatchEventDelegate>
            {
                [typeof(EventExample)] = (_, _, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return ValueTask.FromResult(Result.Success());
                }
            }
        };

        var manager = new OutboxManager(
            outboxStorage,
            eventDispatcher,
            metrics,
            Options.Create(options),
            Options.Create(new OutboxOptions()),
            logger);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act (When)
        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await manager.ProcessPendingAsync(cts.Token));

        // Assert (Then)
        await outboxStorage.DidNotReceive()
            .MarkAsFailedAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<DateTimeOffset?>(),
                Arg.Any<CancellationToken>());
        await outboxStorage.DidNotReceive()
            .MarkAsProcessedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
