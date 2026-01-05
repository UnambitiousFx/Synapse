using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.Application.Domain.Events;

/// <summary>
///     External event - published when an order is cancelled
/// </summary>
public sealed record OrderCancelled : IEvent
{
    public required Guid OrderId { get; init; }
    public required string Reason { get; init; }
    public required DateTime CancelledAt { get; init; }
}