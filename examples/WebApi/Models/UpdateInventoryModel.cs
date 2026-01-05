namespace UnambitiousFx.Examples.WebApi.Models;

public sealed record UpdateInventoryModel
{
    public required int QuantityChange { get; init; }
}