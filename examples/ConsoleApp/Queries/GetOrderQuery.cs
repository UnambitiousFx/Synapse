using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.ConsoleApp.Queries;

// Query for getting an order
public sealed record GetOrderQuery : IRequest<OrderDto>
{
    public required Guid OrderId { get; init; }
}