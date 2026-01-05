using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.Application.Domain.Events;

/// <summary>
///     External event - published to transport layer (RabbitMQ, etc.)
/// </summary>
public sealed record OrderShipped : IEvent
{
    public required Guid OrderId { get; init; }
    public required string TrackingNumber { get; init; }
    public required DateTime ShippedAt { get; init; }
}