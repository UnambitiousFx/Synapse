using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.Application.Domain.Events;

/// <summary>
///     External event - published to transport layer
/// </summary>
public sealed record PaymentProcessed : IEvent
{
    public required Guid PaymentId { get; init; }
    public required Guid OrderId { get; init; }
    public required decimal Amount { get; init; }
    public required string PaymentMethod { get; init; }
    public required DateTime ProcessedAt { get; init; }
}