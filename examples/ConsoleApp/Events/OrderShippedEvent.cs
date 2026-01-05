using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.ConsoleApp.Events;

public sealed record OrderShippedEvent : IEvent
{
    public required Guid OrderId { get; init; }
    public required DateTime ShippedAt { get; init; }
    public required Guid ShippingId { get; init; }
}