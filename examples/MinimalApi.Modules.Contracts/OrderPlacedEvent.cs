using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.MinimalApi.Modules.Contracts;

/// <summary>
///     Published by the Orders module when an order is placed successfully.
///     Consumed by any module that subscribes — without either module knowing about the other.
/// </summary>
/// <remarks>
///     This is the shared contract that decouples Orders from Notifications.
///     Neither module references the other; both reference only this assembly.
/// </remarks>
public sealed record OrderPlacedEvent : IEvent
{
    public required Guid OrderId { get; init; }
    public required string Product { get; init; }
    public required int Quantity { get; init; }
    public required DateTime PlacedAt { get; init; }
}
