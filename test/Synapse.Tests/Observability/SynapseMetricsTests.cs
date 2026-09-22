using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Observability;

namespace UnambitiousFx.Synapse.Tests.Observability;

public sealed class SynapseMetricsTests : IDisposable
{
    private const string MeterName = "Unambitious.Synapse";
    private readonly List<ServiceProvider> _providers = [];

    public void Dispose()
    {
        foreach (var provider in _providers)
        {
            provider.Dispose();
        }
    }

    private static IMeterFactory CreateMeterFactory()
    {
        return new ServiceCollection()
            .AddMetrics()
            .BuildServiceProvider()
            .GetRequiredService<IMeterFactory>();
    }

    // The DI meter factory caches meters by name+version, so re-creating the meter here returns the
    // same instance the SynapseMetrics under test registered its gauges on. Filtering the listener by
    // that instance (not just the name) isolates this test from gauges leaked by other tests' meters.
    private static Meter ResolveMeter(IMeterFactory meterFactory)
        => meterFactory.Create(MeterName, "1.0.0");

    // Mirrors the Singleton-consuming-Scoped-storage shape SynapseMetrics resolves through in
    // production: builds a small provider around the given storage and hands back its scope factory,
    // the same thing DependencyInjectionExtensions.AddSynapse passes to the SynapseMetrics constructor.
    private IServiceScopeFactory ScopeFactoryFor(IEventOutboxStorage storage)
    {
        var services = new ServiceCollection();
        services.AddSingleton(storage);
        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        return provider.GetRequiredService<IServiceScopeFactory>();
    }

    private static (Dictionary<string, double> measurements, MeterListener listener) ListenTo(Meter meter)
    {
        var measurements = new Dictionary<string, double>();
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (ReferenceEquals(instrument.Meter, meter))
                {
                    l.EnableMeasurementEvents(instrument);
                }
            }
        };

        listener.SetMeasurementEventCallback<int>(
            (instrument, value, _, _) => measurements[instrument.Name] = value);
        listener.SetMeasurementEventCallback<double>(
            (instrument, value, _, _) => measurements[instrument.Name] = value);

        return (measurements, listener);
    }

    [Fact]
    public void OutboxGauges_AfterOutboxPopulated_ReportCurrentValues()
    {
        // Arrange (Given)
        var storage = Substitute.For<IEventOutboxStorage>();
        storage.GetPendingCountAsync(Arg.Any<CancellationToken>()).Returns(new ValueTask<int>(5));
        storage.GetOldestPendingAgeAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<TimeSpan?>(TimeSpan.FromSeconds(42)));
        storage.GetRetryingCountAsync(Arg.Any<CancellationToken>()).Returns(new ValueTask<int>(3));
        storage.GetDeadLetterCountAsync(Arg.Any<CancellationToken>()).Returns(new ValueTask<int>(2));

        var meterFactory = CreateMeterFactory();
        _ = new SynapseMetrics(meterFactory, ScopeFactoryFor(storage));

        var (measurements, listener) = ListenTo(ResolveMeter(meterFactory));
        listener.Start();

        // Act (When)
        listener.RecordObservableInstruments();

        // Assert (Then)
        Assert.Equal(5, measurements["mediator.outbox.queue_depth"]);
        Assert.Equal(42, measurements["mediator.outbox.processing_lag"]);
        Assert.Equal(3, measurements["mediator.outbox.retrying_count"]);
        Assert.Equal(2, measurements["mediator.outbox.dead_letter_count"]);

        listener.Dispose();
    }

    [Fact]
    public void OutboxGauges_WhenStorageThrows_FallBackAndCountReadFailure()
    {
        // Arrange (Given)
        var storage = Substitute.For<IEventOutboxStorage>();
        storage.GetPendingCountAsync(Arg.Any<CancellationToken>())
            .Returns<ValueTask<int>>(_ => throw new InvalidOperationException("boom"));
        storage.GetOldestPendingAgeAsync(Arg.Any<CancellationToken>())
            .Returns<ValueTask<TimeSpan?>>(_ => throw new InvalidOperationException("boom"));
        storage.GetRetryingCountAsync(Arg.Any<CancellationToken>())
            .Returns<ValueTask<int>>(_ => throw new InvalidOperationException("boom"));
        storage.GetDeadLetterCountAsync(Arg.Any<CancellationToken>())
            .Returns<ValueTask<int>>(_ => throw new InvalidOperationException("boom"));

        var meterFactory = CreateMeterFactory();
        _ = new SynapseMetrics(meterFactory, ScopeFactoryFor(storage));

        var (measurements, listener) = ListenTo(ResolveMeter(meterFactory));
        var readFailures = 0L;
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
        {
            if (instrument.Name == "mediator.outbox.metrics.read_failures")
            {
                readFailures += value;
            }
        });
        listener.Start();

        // Act (When)
        listener.RecordObservableInstruments();

        // Assert (Then) — gauges fall back to last-known (0) and read failures are counted
        Assert.Equal(0, measurements["mediator.outbox.queue_depth"]);
        Assert.Equal(0, measurements["mediator.outbox.processing_lag"]);
        Assert.Equal(0, measurements["mediator.outbox.retrying_count"]);
        Assert.Equal(0, measurements["mediator.outbox.dead_letter_count"]);
        Assert.Equal(4, readFailures);

        listener.Dispose();
    }

    [Fact]
    public void OutboxGauges_WithGenuinelyAsyncStorage_ReportCurrentValues()
    {
        // Arrange (Given) — a storage whose ValueTasks actually go async, as any real
        // database-backed storage does. The observable-gauge callback is synchronous, so it must
        // block on the read inside its scope; sampling the ValueTask only when it happens to have
        // completed synchronously would report the last-known (zero) value forever and dispose the
        // scope out from under the in-flight query.
        var storage = new AsyncOutboxStorage(
            pendingCount: 5,
            oldestPendingAge: TimeSpan.FromSeconds(42),
            retryingCount: 3,
            deadLetterCount: 2);

        var meterFactory = CreateMeterFactory();
        _ = new SynapseMetrics(meterFactory, ScopeFactoryFor(storage));

        var (measurements, listener) = ListenTo(ResolveMeter(meterFactory));
        listener.Start();

        // Act (When)
        listener.RecordObservableInstruments();

        // Assert (Then)
        Assert.Equal(5, measurements["mediator.outbox.queue_depth"]);
        Assert.Equal(42, measurements["mediator.outbox.processing_lag"]);
        Assert.Equal(3, measurements["mediator.outbox.retrying_count"]);
        Assert.Equal(2, measurements["mediator.outbox.dead_letter_count"]);

        // ...and every read really did go async, so the values above cannot have come from a
        // synchronously-completed ValueTask.
        Assert.Equal(4, storage.AsynchronousReads);

        listener.Dispose();
    }

    [Fact]
    public void OutboxGauges_WithoutStorage_ReportZero()
    {
        // Arrange (Given)
        var meterFactory = CreateMeterFactory();
        _ = new SynapseMetrics(meterFactory);

        var (measurements, listener) = ListenTo(ResolveMeter(meterFactory));
        listener.Start();

        // Act (When)
        listener.RecordObservableInstruments();

        // Assert (Then)
        Assert.Equal(0, measurements["mediator.outbox.queue_depth"]);
        Assert.Equal(0, measurements["mediator.outbox.processing_lag"]);
        Assert.Equal(0, measurements["mediator.outbox.retrying_count"]);
        Assert.Equal(0, measurements["mediator.outbox.dead_letter_count"]);

        listener.Dispose();
    }

    // A storage whose reads genuinely complete asynchronously, the way a database-backed one does.
    // ConfigureAwait(false) keeps the continuation on the thread pool so blocking on it from the
    // (synchronous) gauge callback cannot deadlock against any ambient SynchronizationContext.
    private sealed class AsyncOutboxStorage(
        int pendingCount,
        TimeSpan oldestPendingAge,
        int retryingCount,
        int deadLetterCount) : IEventOutboxStorage
    {
        private int _asynchronousReads;

        public int AsynchronousReads => Volatile.Read(ref _asynchronousReads);

        private async ValueTask<T> ReadAsync<T>(T value)
        {
            await Task.Delay(1).ConfigureAwait(false);
            Interlocked.Increment(ref _asynchronousReads);
            return value;
        }

        public ValueTask<int> GetPendingCountAsync(CancellationToken cancellationToken = default)
            => ReadAsync(pendingCount);

        public ValueTask<TimeSpan?> GetOldestPendingAgeAsync(CancellationToken cancellationToken = default)
            => ReadAsync<TimeSpan?>(oldestPendingAge);

        public ValueTask<int> GetRetryingCountAsync(CancellationToken cancellationToken = default)
            => ReadAsync(retryingCount);

        public ValueTask<int> GetDeadLetterCountAsync(CancellationToken cancellationToken = default)
            => ReadAsync(deadLetterCount);

        public ValueTask<UnambitiousFx.Functional.Result> AddAsync<TEvent>(TEvent @event,
            IReadOnlyDictionary<string, string> headers,
            CancellationToken cancellationToken = default)
            where TEvent : class, IEvent
            => new(UnambitiousFx.Functional.Result.Success());

        public ValueTask<IReadOnlyList<OutboxEntry>> GetPendingEventsAsync(
            CancellationToken cancellationToken = default)
            => new(Array.Empty<OutboxEntry>() as IReadOnlyList<OutboxEntry>);

        public ValueTask<UnambitiousFx.Functional.Result> MarkAsProcessedAsync(Guid id,
            CancellationToken cancellationToken = default)
            => new(UnambitiousFx.Functional.Result.Success());

        public ValueTask<UnambitiousFx.Functional.Result> ClearAsync(
            CancellationToken cancellationToken = default)
            => new(UnambitiousFx.Functional.Result.Success());

        public ValueTask<UnambitiousFx.Functional.Result> MarkAsFailedAsync(Guid id,
            string reason,
            bool deadLetter,
            DateTimeOffset? nextAttemptAt = null,
            CancellationToken cancellationToken = default)
            => new(UnambitiousFx.Functional.Result.Success());

        public ValueTask<IReadOnlyList<OutboxEntry>> GetDeadLetterEventsAsync(
            CancellationToken cancellationToken = default)
            => new(Array.Empty<OutboxEntry>() as IReadOnlyList<OutboxEntry>);

        public ValueTask<int?> GetAttemptCountAsync(Guid id,
            CancellationToken cancellationToken = default)
            => new((int?)null);
    }
}
