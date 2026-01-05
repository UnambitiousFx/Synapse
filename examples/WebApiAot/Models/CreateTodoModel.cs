namespace UnambitiousFx.Examples.WebApiAot.Models;

public sealed record CreateTodoModel
{
    public required string Name { get; init; }
}