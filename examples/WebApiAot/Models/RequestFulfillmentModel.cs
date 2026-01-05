namespace UnambitiousFx.Examples.WebApiAot.Models;

public sealed record RequestFulfillmentModel
{
    public required Guid OrderId { get; init; }
    public required string WarehouseLocation { get; init; }
}