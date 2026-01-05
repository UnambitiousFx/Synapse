using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.Application.Domain.Events;

/// <summary>
///     External event - published to transport layer
///     Can be consumed by inventory management systems
/// </summary>
public sealed record InventoryUpdated : IEvent
{
    public required Guid ProductId { get; init; }
    public required int QuantityChange { get; init; }
    public required int NewQuantity { get; init; }
    public required DateTime UpdatedAt { get; init; }
}