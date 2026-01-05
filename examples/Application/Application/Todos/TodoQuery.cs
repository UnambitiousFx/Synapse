using UnambitiousFx.Examples.Application.Domain.Entities;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.Application.Application.Todos;

public sealed record TodoQuery : IRequest<Todo>
{
    public required Guid Id { get; init; }
}