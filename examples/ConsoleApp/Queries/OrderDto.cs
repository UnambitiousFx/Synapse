namespace UnambitiousFx.Examples.ConsoleApp.Queries;

public sealed record OrderDto
{
    public required Guid Id { get; init; }
    public required string ProductName { get; init; }
    public required int Quantity { get; init; }
    public required decimal TotalAmount { get; init; }
    public required DateTime CreatedAt { get; init; }
}