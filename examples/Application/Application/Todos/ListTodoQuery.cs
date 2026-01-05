using UnambitiousFx.Examples.Application.Domain.Entities;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.Application.Application.Todos;

public record ListTodoQuery : IRequest<IEnumerable<Todo>>;