using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.Application.Application.Todos;

public sealed record CreateTodoCommand : IRequest<Guid>
{
    public required string Name { get; init; }
}