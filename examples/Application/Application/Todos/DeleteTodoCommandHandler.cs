using UnambitiousFx.Examples.Application.Domain.Events;
using UnambitiousFx.Examples.Application.Domain.Repositories;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.Application.Application.Todos;

public sealed class DeleteTodoCommandHandler : IRequestHandler<DeleteTodoCommand>
{
    private readonly IContext _context;
    private readonly ITodoRepository _todoRepository;

    public DeleteTodoCommandHandler(ITodoRepository todoRepository,
        IContext context)
    {
        _todoRepository = todoRepository;
        _context = context;
    }

    public async ValueTask<Result> HandleAsync(DeleteTodoCommand request,
        CancellationToken cancellationToken = default)
    {
        var todoOpt = await _todoRepository.GetAsync(request.Id, cancellationToken);

        if (!todoOpt.Some(out var todo)) return Result.Failure("Not found");

        await _todoRepository.DeleteAsync(todo.Id, cancellationToken);

        await _context.PublishEventAsync(new TodoDeleted
        {
            Todo = todo
        }, cancellationToken);

        return Result.Success();
    }
}