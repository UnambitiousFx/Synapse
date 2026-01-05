using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.Application.Application.Orders;

public sealed record CancelOrderCommand : IRequest
{
    public required Guid OrderId { get; init; }
    public required string Reason { get; init; }
}