using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.ConsoleApp.Events;

public sealed record MetricsEvent : IEvent
{
    public required string MetricName { get; init; }
    public required double Value { get; init; }
}