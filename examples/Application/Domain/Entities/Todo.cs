namespace UnambitiousFx.Examples.Application.Domain.Entities;

public record Todo
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}