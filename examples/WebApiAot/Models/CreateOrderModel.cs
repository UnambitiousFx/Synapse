namespace UnambitiousFx.Examples.WebApiAot.Models;

public sealed record CreateOrderModel
{
    public required string CustomerName { get; init; }
    public required decimal TotalAmount { get; init; }
}