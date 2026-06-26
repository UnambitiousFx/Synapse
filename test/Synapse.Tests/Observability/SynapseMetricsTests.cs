using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Observability;

namespace UnambitiousFx.Synapse.Tests.Observability;

public sealed class SynapseMetricsTests
{
    private const string MeterName = "Unambitious.Synapse";

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
        _ = new SynapseMetrics(meterFactory, storage);

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
        _ = new SynapseMetrics(meterFactory, storage);

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
}
