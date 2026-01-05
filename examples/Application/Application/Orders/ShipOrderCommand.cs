using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.Application.Application.Orders;

public sealed record ShipOrderCommand : IRequest
{
    public required Guid OrderId { get; init; }
}