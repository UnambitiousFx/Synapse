using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.Application.Application.Todos;

public record DeleteTodoCommand : IRequest
{
    public required Guid Id { get; init; }
}