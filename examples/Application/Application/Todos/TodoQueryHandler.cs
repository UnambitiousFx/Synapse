using UnambitiousFx.Examples.Application.Domain.Entities;
using UnambitiousFx.Examples.Application.Domain.Repositories;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.Application.Application.Todos;

public sealed class TodoQueryHandler : IRequestHandler<TodoQuery, Todo>
{
    private readonly ITodoRepository _todoRepository;

    public TodoQueryHandler(ITodoRepository todoRepository)
    {
        _todoRepository = todoRepository;
    }

    public async ValueTask<Result<Todo>> HandleAsync(TodoQuery request,
        CancellationToken cancellationToken = default)
    {
        var todoOpt = await _todoRepository.GetAsync(request.Id, cancellationToken);

        return todoOpt.Some(out var todo)
            ? Result.Success(todo)
            : Result.Failure<Todo>(new Exception("Not found"));
    }
}