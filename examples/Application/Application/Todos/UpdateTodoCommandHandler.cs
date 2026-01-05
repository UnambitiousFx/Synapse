using UnambitiousFx.Examples.Application.Domain.Events;
using UnambitiousFx.Examples.Application.Domain.Repositories;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.Application.Application.Todos;

[RequestHandler<UpdateTodoCommand>]
public sealed class UpdateTodoCommandHandler : IRequestHandler<UpdateTodoCommand>
{
    private readonly IContext _context;
    private readonly ITodoRepository _todoRepository;

    public UpdateTodoCommandHandler(ITodoRepository todoRepository,
        IContext context)
    {
        _todoRepository = todoRepository;
        _context = context;
    }

    public async ValueTask<Result> HandleAsync(UpdateTodoCommand request,
        CancellationToken cancellationToken = default)
    {
        var todoOpt = await _todoRepository.GetAsync(request.Id, cancellationToken);

        if (!todoOpt.Some(out var todo)) return Result.Failure("Not found");

        todo.Name = request.Name;

        await _todoRepository.UpdateAsync(todo, cancellationToken);
        await _context.PublishEventAsync(new TodoUpdated
        {
            Todo = todo
        }, cancellationToken);

        return Result.Success();
    }
}