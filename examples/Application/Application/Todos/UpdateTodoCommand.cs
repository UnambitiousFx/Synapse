using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.Application.Application.Todos;

public record UpdateTodoCommand : IRequest
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
}