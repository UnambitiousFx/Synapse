using System.Collections.Concurrent;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Publish.Outbox;
using UnambitiousFx.Synapse.Tests.Definitions;

namespace UnambitiousFx.Synapse.Tests.Publish.Outbox;

[TestSubject(typeof(OutboxDispatcherService))]
public sealed class OutboxDispatcherServiceTests
{
    private static readonly TimeSpan FastPolling = TimeSpan.FromMilliseconds(20);

    [Fact]
    public async Task AddOutboxDispatcher_DispatchesOutboxEventsWithoutAnExplicitCommit()
    {
        // Arrange (Given)
        var recorder = new EventRecorder();
        await using var provider = BuildProvider(recorder,
            cfg => cfg.AddOutboxDispatcher(o => o.PollingInterval = FastPolling));
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IEmitter>()
                .EmitAsync(new EventExample("background"), EmitMode.Outbox, TestContext.Current.CancellationToken);
        }

        // Act (When)
        var dispatcher = await StartDispatcherAsync(provider);
        var dispatched = await WaitUntilAsync(() => recorder.Dispatched.Count == 1);
        await dispatcher.StopAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(dispatched);
        Assert.Equal(["background"], recorder.Dispatched);
        var storage = provider.GetRequiredService<IEventOutboxStorage>();
        Assert.Equal(0, await storage.GetPendingCountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AddOutboxDispatcher_WithABatchSize_DrainsEveryBatchWithoutWaitingForTheInterval()
    {
        // Arrange (Given) — a polling interval far longer than the test, so only back-to-back polls can drain it
        var recorder = new EventRecorder();
        await using var provider = BuildProvider(recorder, cfg =>
        {
            cfg.ConfigureOutbox(o => o.BatchSize = 2);
            cfg.AddOutboxDispatcher(o => o.PollingInterval = TimeSpan.FromHours(1));
        });
        await using (var scope = provider.CreateAsyncScope())
        {
            var emitter = scope.ServiceProvider.GetRequiredService<IEmitter>();
            for (var i = 0; i < 5; i++)
            {
                await emitter.EmitAsync(new EventExample($"event-{i}"), EmitMode.Outbox,
                    TestContext.Current.CancellationToken);
            }
        }

        // Act (When)
        var dispatcher = await StartDispatcherAsync(provider);
        var drained = await WaitUntilAsync(() => recorder.Dispatched.Count == 5);
        await dispatcher.StopAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(drained);
    }

    [Fact]
    public async Task AddOutboxDispatcher_WhenAPollThrows_KeepsPolling()
    {
        // Arrange (Given) — the storage is unavailable for the first poll only
        var storage = Substitute.For<IEventOutboxStorage, IClaimableOutboxStorage>();
        var claimable = (IClaimableOutboxStorage)storage;
        var polls = 0;
        claimable.ClaimPendingEventsAsync(Arg.Any<int?>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(_ => Interlocked.Increment(ref polls) == 1
                ? throw new InvalidOperationException("storage unavailable")
                : new ValueTask<IReadOnlyList<OutboxEntry>>(Array.Empty<OutboxEntry>()));
        await using var provider = BuildProvider(new EventRecorder(), cfg =>
        {
            cfg.AddOutboxDispatcher(o => o.PollingInterval = FastPolling);
        }, storage);

        // Act (When)
        var dispatcher = await StartDispatcherAsync(provider);
        var recovered = await WaitUntilAsync(() => Volatile.Read(ref polls) >= 2);
        await dispatcher.StopAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(recovered);
    }

    [Fact]
    public async Task AddOutboxDispatcher_WithANonPositivePollingInterval_FailsTheService()
    {
        // Arrange (Given)
        await using var provider = BuildProvider(new EventRecorder(),
            cfg => cfg.AddOutboxDispatcher(o => o.PollingInterval = TimeSpan.Zero));

        // Act (When)
        var dispatcher = await StartDispatcherAsync(provider);

        // Assert (Then)
        Assert.NotNull(dispatcher.ExecuteTask);
        await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.ExecuteTask!);
    }

    [Fact]
    public async Task AddSynapse_WithoutAddOutboxDispatcher_RegistersNoDispatcher()
    {
        // Arrange (Given)
        await using var provider = BuildProvider(new EventRecorder(), _ => { });

        // Act (When)
        var hostedServices = provider.GetServices<IHostedService>();

        // Assert (Then)
        Assert.DoesNotContain(hostedServices, service => service is OutboxDispatcherService);
    }

    private static ServiceProvider BuildProvider(EventRecorder recorder,
        Action<ISynapseConfig> configure,
        IEventOutboxStorage? storage = null)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton(recorder);
        services.AddSynapse(cfg =>
        {
            cfg.RegisterEventHandler<RecordingEventHandler, EventExample>();
            configure(cfg);
        });
        if (storage is not null)
        {
            services.AddSingleton(storage);
        }

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static async Task<OutboxDispatcherService> StartDispatcherAsync(IServiceProvider provider)
    {
        var dispatcher = provider.GetServices<IHostedService>().OfType<OutboxDispatcherService>().Single();
        await dispatcher.StartAsync(TestContext.Current.CancellationToken);
        return dispatcher;
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        return condition();
    }

    private sealed class EventRecorder
    {
        private readonly ConcurrentQueue<string> _dispatched = new();

        public IReadOnlyCollection<string> Dispatched => _dispatched;

        public void Add(string name)
        {
            _dispatched.Enqueue(name);
        }
    }

    private sealed class RecordingEventHandler(EventRecorder recorder) : IEventHandler<EventExample>
    {
        public ValueTask<Result> HandleAsync(EventExample @event, CancellationToken cancellationToken = default)
        {
            recorder.Add(@event.Name);
            return new ValueTask<Result>(Result.Success());
        }
    }
}
