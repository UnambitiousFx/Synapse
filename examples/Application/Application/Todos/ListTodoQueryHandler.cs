using UnambitiousFx.Examples.Application.Domain.Entities;
using UnambitiousFx.Examples.Application.Domain.Repositories;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.Application.Application.Todos;

[RequestHandler<ListTodoQuery, IEnumerable<Todo>>]
public sealed class ListTodoQueryHandler : IRequestHandler<ListTodoQuery, IEnumerable<Todo>>
{
    private readonly ITodoRepository _todoRepository;

    public ListTodoQueryHandler(ITodoRepository todoRepository)
    {
        _todoRepository = todoRepository;
    }

    public async ValueTask<Result<IEnumerable<Todo>>> HandleAsync(ListTodoQuery request,
        CancellationToken cancellationToken = default)
    {
        var todos = await _todoRepository.GetAllAsync(cancellationToken);

        return Result.Success(todos);
    }
}