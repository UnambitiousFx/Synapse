namespace UnambitiousFx.Examples.WebApiAot.Models;

public sealed record UpdateTodoModel
{
    public required string Name { get; init; }
}