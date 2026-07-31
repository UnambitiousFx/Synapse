using System.Diagnostics;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Contexts;
using UnambitiousFx.Synapse.Observability;
using UnambitiousFx.Synapse.Propagation;
using UnambitiousFx.Synapse.Publish;
using UnambitiousFx.Synapse.Publish.Outbox;
using UnambitiousFx.Synapse.Tests.Definitions;

namespace UnambitiousFx.Synapse.Tests.Publish.Outbox;

[TestSubject(typeof(OutboxManager))]
public sealed class OutboxManagerTests
{
    private static OutboxManager BuildManager(
        IEventOutboxStorage outboxStorage,
        IEventDispatcher eventDispatcher,
        ISynapseMetrics metrics,
        ILogger<OutboxManager> logger,
        EventDispatcherOptions? dispatcherOptions = null,
        OutboxOptions? outboxOptions = null,
        IContextPropagator? propagator = null,
        IContextAccessor? contextAccessor = null)
    {
        return new OutboxManager(
            outboxStorage,
            DispatchScopes.For(_ => eventDispatcher),
            metrics,
            propagator ?? new W3CContextPropagator(),
            contextAccessor ?? new StubContextAccessor(null, false),
            Options.Create(dispatcherOptions ?? new EventDispatcherOptions()),
            Options.Create(outboxOptions ?? new OutboxOptions()),
            logger);
    }

    private static (IEventOutboxStorage, IEventDispatcher, ISynapseMetrics, ILogger<OutboxManager>) CreateSubstitutes()
    {
        return (
            Substitute.For<IEventOutboxStorage>(),
            Substitute.For<IEventDispatcher>(),
            Substitute.For<ISynapseMetrics>(),
            Substitute.For<ILogger<OutboxManager>>()
        );
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenCanceled_DoesNotMarkEventAsFailed()
    {
        // Arrange (Given)
        var (outboxStorage, eventDispatcher, metrics, logger) = CreateSubstitutes();
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

        var manager = BuildManager(outboxStorage, eventDispatcher, metrics, logger, options);
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

    [Fact]
    public async Task ProcessPendingAsync_WhenNoPendingEvents_ReturnsSuccess()
    {
        // Arrange (Given)
        var (outboxStorage, eventDispatcher, metrics, logger) = CreateSubstitutes();
        outboxStorage.GetPendingEventsAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<OutboxEntry>>(Array.Empty<OutboxEntry>()));
        var manager = BuildManager(outboxStorage, eventDispatcher, metrics, logger);

        // Act (When)
        var result = await manager.ProcessPendingAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        await outboxStorage.DidNotReceive().MarkAsProcessedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenDispatchSucceeds_MarksProcessedAndRecordsMetric()
    {
        // Arrange (Given)
        var (outboxStorage, eventDispatcher, metrics, logger) = CreateSubstitutes();
        var @event = new EventExample("success-event");
        var entryId = Guid.NewGuid();

        outboxStorage.GetPendingEventsAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<OutboxEntry>>(
                new[] { new OutboxEntry(entryId, @event) }));
        outboxStorage.MarkAsProcessedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Success()));

        var options = new EventDispatcherOptions
        {
            Dispatchers = new Dictionary<Type, DispatchEventDelegate>
            {
                [typeof(EventExample)] = (_, _, _) => ValueTask.FromResult(Result.Success())
            }
        };
        var manager = BuildManager(outboxStorage, eventDispatcher, metrics, logger, options);

        // Act (When)
        var result = await manager.ProcessPendingAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        await outboxStorage.Received(1).MarkAsProcessedAsync(entryId, Arg.Any<CancellationToken>());
        metrics.Received(1).RecordOutboxEventProcessed(nameof(EventExample), true);
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenNoDispatcherRegistered_ReturnsFailure()
    {
        // Arrange (Given)
        var (outboxStorage, eventDispatcher, metrics, logger) = CreateSubstitutes();
        var @event = new EventExample("no-dispatcher");

        outboxStorage.GetPendingEventsAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<OutboxEntry>>(
                new[] { new OutboxEntry(Guid.NewGuid(), @event) }));

        // empty Dispatchers dict — no delegate registered for EventExample
        var options = new EventDispatcherOptions { Dispatchers = new Dictionary<Type, DispatchEventDelegate>() };
        var manager = BuildManager(outboxStorage, eventDispatcher, metrics, logger, options);

        // Act (When)
        var result = await manager.ProcessPendingAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.False(result.IsSuccess);
        await outboxStorage.DidNotReceive().MarkAsProcessedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenDispatchFails_BelowMaxRetries_WithRetryDelay_SchedulesRetry()
    {
        // Arrange (Given)
        var (outboxStorage, eventDispatcher, metrics, logger) = CreateSubstitutes();
        var @event = new EventExample("fail-event");

        outboxStorage.GetPendingEventsAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<OutboxEntry>>(
                new[] { new OutboxEntry(Guid.NewGuid(), @event) }));
        outboxStorage.GetAttemptCountAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<int?>(0));
        outboxStorage.MarkAsFailedAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<DateTimeOffset?>(),
                Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Success()));

        var options = new EventDispatcherOptions
        {
            Dispatchers = new Dictionary<Type, DispatchEventDelegate>
            {
                [typeof(EventExample)] = (_, _, _) => ValueTask.FromResult(Result.Failure("dispatch error"))
            }
        };
        var outboxOptions = new OutboxOptions
        {
            MaxRetryAttempts = 3,
            InitialRetryDelay = TimeSpan.FromSeconds(1),
            BackoffFactor = 2d
        };
        var manager = BuildManager(outboxStorage, eventDispatcher, metrics, logger, options, outboxOptions);

        // Act (When)
        var result = await manager.ProcessPendingAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.False(result.IsSuccess);
        metrics.Received(1).RecordOutboxEventProcessed(nameof(EventExample), false);
        metrics.DidNotReceive().RecordOutboxDeadLettered(Arg.Any<string>());
        await outboxStorage.Received(1).MarkAsFailedAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), false,
            Arg.Is<DateTimeOffset?>(d => d.HasValue),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenDispatchFails_AtMaxRetries_DeadLetters()
    {
        // Arrange (Given)
        var (outboxStorage, eventDispatcher, metrics, logger) = CreateSubstitutes();
        var @event = new EventExample("dead-letter-event");

        outboxStorage.GetPendingEventsAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<OutboxEntry>>(
                new[] { new OutboxEntry(Guid.NewGuid(), @event) }));
        // attemptCount = 2, nextAttemptNumber = 3 >= MaxRetryAttempts = 3 → dead letter
        outboxStorage.GetAttemptCountAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<int?>(2));
        outboxStorage.MarkAsFailedAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<DateTimeOffset?>(),
                Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Success()));

        var options = new EventDispatcherOptions
        {
            Dispatchers = new Dictionary<Type, DispatchEventDelegate>
            {
                [typeof(EventExample)] = (_, _, _) => ValueTask.FromResult(Result.Failure("dispatch failed again"))
            }
        };
        var outboxOptions = new OutboxOptions { MaxRetryAttempts = 3, InitialRetryDelay = TimeSpan.FromSeconds(1) };
        var manager = BuildManager(outboxStorage, eventDispatcher, metrics, logger, options, outboxOptions);

        // Act (When)
        await manager.ProcessPendingAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        await outboxStorage.Received(1).MarkAsFailedAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), true, Arg.Is<DateTimeOffset?>(d => !d.HasValue),
            Arg.Any<CancellationToken>());
        metrics.Received(1).RecordOutboxDeadLettered(nameof(EventExample));
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenDispatchFails_NoRetryDelay_MarksFailedWithoutNextAttemptTime()
    {
        // Arrange (Given)
        var (outboxStorage, eventDispatcher, metrics, logger) = CreateSubstitutes();
        var @event = new EventExample("no-delay-event");

        outboxStorage.GetPendingEventsAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<OutboxEntry>>(
                new[] { new OutboxEntry(Guid.NewGuid(), @event) }));
        outboxStorage.GetAttemptCountAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<int?>(0));
        outboxStorage.MarkAsFailedAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<DateTimeOffset?>(),
                Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Success()));

        var options = new EventDispatcherOptions
        {
            Dispatchers = new Dictionary<Type, DispatchEventDelegate>
            {
                [typeof(EventExample)] = (_, _, _) => ValueTask.FromResult(Result.Failure("fail"))
            }
        };
        // InitialRetryDelay = Zero → no exponential backoff, nextAttemptAt = null
        var outboxOptions = new OutboxOptions
        {
            MaxRetryAttempts = 3,
            InitialRetryDelay = TimeSpan.Zero
        };
        var manager = BuildManager(outboxStorage, eventDispatcher, metrics, logger, options, outboxOptions);

        // Act (When)
        await manager.ProcessPendingAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        await outboxStorage.Received(1).MarkAsFailedAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), false, Arg.Is<DateTimeOffset?>(d => !d.HasValue),
            Arg.Any<CancellationToken>());
        metrics.DidNotReceive().RecordOutboxDeadLettered(Arg.Any<string>());
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenDispatcherThrows_ReturnsFailureAndMarksEventFailed()
    {
        // Arrange (Given)
        var (outboxStorage, eventDispatcher, metrics, logger) = CreateSubstitutes();
        var @event = new EventExample("throw-event");

        outboxStorage.GetPendingEventsAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<OutboxEntry>>(
                new[] { new OutboxEntry(Guid.NewGuid(), @event) }));
        outboxStorage.GetAttemptCountAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<int?>(0));
        outboxStorage.MarkAsFailedAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<DateTimeOffset?>(),
                Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Success()));

        var options = new EventDispatcherOptions
        {
            Dispatchers = new Dictionary<Type, DispatchEventDelegate>
            {
                [typeof(EventExample)] = (_, _, _) => throw new InvalidOperationException("boom")
            }
        };
        var manager = BuildManager(outboxStorage, eventDispatcher, metrics, logger, options);

        // Act (When)
        var result = await manager.ProcessPendingAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.False(result.IsSuccess);
        await outboxStorage.Received(1).MarkAsFailedAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<DateTimeOffset?>(),
            Arg.Any<CancellationToken>());
        await outboxStorage.DidNotReceive().MarkAsProcessedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessPendingAsync_RespectsBatchSize()
    {
        // Arrange (Given)
        var (outboxStorage, eventDispatcher, metrics, logger) = CreateSubstitutes();

        var entries = new OutboxEntry[]
        {
            new(Guid.NewGuid(), new EventExample("e1")),
            new(Guid.NewGuid(), new EventExample("e2")),
            new(Guid.NewGuid(), new EventExample("e3"))
        };
        outboxStorage.GetPendingEventsAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<OutboxEntry>>(entries));
        outboxStorage.MarkAsProcessedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Success()));

        var options = new EventDispatcherOptions
        {
            Dispatchers = new Dictionary<Type, DispatchEventDelegate>
            {
                [typeof(EventExample)] = (_, _, _) => ValueTask.FromResult(Result.Success())
            }
        };
        // BatchSize = 2: only the first 2 entries should be processed
        var outboxOptions = new OutboxOptions { BatchSize = 2 };
        var manager = BuildManager(outboxStorage, eventDispatcher, metrics, logger, options, outboxOptions);

        // Act (When)
        await manager.ProcessPendingAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        await outboxStorage.Received(2).MarkAsProcessedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StoreAsync_DelegatesToOutboxStorage()
    {
        // Arrange (Given)
        var (outboxStorage, eventDispatcher, metrics, logger) = CreateSubstitutes();
        var @event = new EventExample("store-me");
        outboxStorage.AddAsync(@event, Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Success()));
        var manager = BuildManager(outboxStorage, eventDispatcher, metrics, logger);

        // Act (When)
        var result = await manager.StoreAsync(@event, TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        await outboxStorage.Received(1).AddAsync(@event,
            Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StoreAsync_WhenAContextExists_CapturesItsBaggageAsHeaders()
    {
        // Arrange (Given)
        var (outboxStorage, eventDispatcher, metrics, logger) = CreateSubstitutes();
        var @event = new EventExample("store-me");
        IReadOnlyDictionary<string, string>? captured = null;
        outboxStorage.AddAsync(@event, Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                captured = call.Arg<IReadOnlyDictionary<string, string>>();
                return ValueTask.FromResult(Result.Success());
            });

        var context = new Context(
            new ContextIdentity(ActivityTraceId.CreateRandom().ToHexString(), null, DateTimeOffset.UtcNow));
        context.SetBaggage("tenant.id", "contoso");

        var manager = BuildManager(outboxStorage, eventDispatcher, metrics, logger,
            contextAccessor: new StubContextAccessor(context, true));

        // Act (When)
        await manager.StoreAsync(@event, TestContext.Current.CancellationToken);

        // Assert (Then) — business values only; identity travels as traceparent when a span is recording
        Assert.NotNull(captured);
        var baggage = captured![PropagationKeys.Baggage];
        Assert.Equal("tenant.id=contoso", baggage);
        Assert.DoesNotContain("synapse.", baggage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StoreAsync_WhenNoContextExists_CapturesNoHeadersAndDoesNotCreateOne()
    {
        // Arrange (Given) — reading IContextAccessor.Context is what creates a context, so storing an event
        // outside any unit of work must check IsInitialized rather than invent a flow for the entry to belong to
        var (outboxStorage, eventDispatcher, metrics, logger) = CreateSubstitutes();
        var @event = new EventExample("store-me");
        IReadOnlyDictionary<string, string>? captured = null;
        outboxStorage.AddAsync(@event, Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                captured = call.Arg<IReadOnlyDictionary<string, string>>();
                return ValueTask.FromResult(Result.Success());
            });

        var accessor = new StubContextAccessor(null, false);
        var manager = BuildManager(outboxStorage, eventDispatcher, metrics, logger, contextAccessor: accessor);

        // Act (When)
        await manager.StoreAsync(@event, TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.NotNull(captured);
        Assert.Empty(captured!);
        Assert.False(accessor.ContextWasRead);
    }
}
