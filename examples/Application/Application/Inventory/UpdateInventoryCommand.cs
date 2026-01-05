using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.Application.Application.Inventory;

public sealed record UpdateInventoryCommand : IRequest
{
    public required Guid ProductId { get; init; }
    public required int QuantityChange { get; init; }
}