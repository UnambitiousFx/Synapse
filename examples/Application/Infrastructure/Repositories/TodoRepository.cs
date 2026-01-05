using UnambitiousFx.Examples.Application.Domain.Entities;
using UnambitiousFx.Examples.Application.Domain.Repositories;
using UnambitiousFx.Functional;

namespace UnambitiousFx.Examples.Application.Infrastructure.Repositories;

public sealed class TodoRepository : ITodoRepository
{
    private static readonly Dictionary<Guid, Todo> Todos = new();

    public ValueTask CreateAsync(Todo todo,
        CancellationToken cancellationToken = default)
    {
        Todos.Add(todo.Id, todo);
        return ValueTask.CompletedTask;
    }

    public ValueTask UpdateAsync(Todo todo,
        CancellationToken cancellationToken = default)
    {
        Todos[todo.Id] = todo;
        return ValueTask.CompletedTask;
    }

    public ValueTask<Maybe<Todo>> GetAsync(Guid id,
        CancellationToken cancellationToken = default)
    {
        if (Todos.TryGetValue(id, out var todo)) return ValueTask.FromResult(Maybe<Todo>.Some(todo));

        return ValueTask.FromResult(Maybe<Todo>.None());
    }

    public ValueTask<IEnumerable<Todo>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult<IEnumerable<Todo>>(Todos.Values);
    }

    public ValueTask DeleteAsync(Guid id,
        CancellationToken cancellationToken)
    {
        Todos.Remove(id);

        return ValueTask.CompletedTask;
    }
}