namespace UnambitiousFx.Examples.WebApiAot.Models;

public sealed record UpdateInventoryModel
{
    public required int QuantityChange { get; init; }
}