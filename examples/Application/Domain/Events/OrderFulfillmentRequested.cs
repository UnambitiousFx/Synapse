using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.Application.Domain.Events;

/// <summary>
///     External event - published when fulfillment is requested
/// </summary>
public sealed record OrderFulfillmentRequested : IEvent
{
    public required Guid OrderId { get; init; }
    public required Guid FulfillmentId { get; init; }
    public required string WarehouseLocation { get; init; }
    public required DateTime RequestedAt { get; init; }
}