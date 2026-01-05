namespace UnambitiousFx.Examples.WebApi.Models;

public sealed record CancelOrderModel
{
    public required string Reason { get; init; }
}