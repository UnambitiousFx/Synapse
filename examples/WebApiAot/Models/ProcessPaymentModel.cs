namespace UnambitiousFx.Examples.WebApiAot.Models;

public sealed record ProcessPaymentModel
{
    public required Guid OrderId { get; init; }
    public required decimal Amount { get; init; }
    public required string PaymentMethod { get; init; }
}