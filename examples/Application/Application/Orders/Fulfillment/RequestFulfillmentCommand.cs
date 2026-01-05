using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.Application.Application.Orders.Fulfillment;

public sealed record RequestFulfillmentCommand : IRequest<Guid>
{
    public required Guid OrderId { get; init; }
    public required string WarehouseLocation { get; init; }
}