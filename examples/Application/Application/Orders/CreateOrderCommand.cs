using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.Application.Application.Orders;

public sealed record CreateOrderCommand : IRequest<Guid>
{
    public required string CustomerName { get; init; }
    public required decimal TotalAmount { get; init; }
}