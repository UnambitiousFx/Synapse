namespace UnambitiousFx.Examples.ConsoleApp.Commands;

public sealed record OrderCreatedResponse
{
    public required Guid OrderId { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required decimal TotalAmount { get; init; }
}