using UnambitiousFx.Examples.GettingStarted.Domain;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.GettingStarted.Step1_BasicCommands;

// ═══════════════════════════════════════════════════════════════
// Command Handlers
// ═══════════════════════════════════════════════════════════════

/// <summary>
///     Handler for commands without response
/// </summary>
[RequestHandler<CreateTaskCommand>]
public sealed class CreateTaskCommandHandler : IRequestHandler<CreateTaskCommand>
{
    private readonly TaskRepository _repository;

    public CreateTaskCommandHandler(TaskRepository repository)
    {
        _repository = repository;
    }

    public ValueTask<Result> HandleAsync(CreateTaskCommand request, CancellationToken cancellationToken = default)
    {
        // Create a task without returning its ID
        _repository.Create(request.Title, request.Description);
        return ValueTask.FromResult(Result.Success());
    }
}

/// <summary>
///     Handler for commands with response
/// </summary>
[RequestHandler<CreateTaskWithIdCommand, Guid>]
public sealed class CreateTaskWithIdCommandHandler : IRequestHandler<CreateTaskWithIdCommand, Guid>
{
    private readonly TaskRepository _repository;

    public CreateTaskWithIdCommandHandler(TaskRepository repository)
    {
        _repository = repository;
    }

    public ValueTask<Result<Guid>> HandleAsync(CreateTaskWithIdCommand request,
        CancellationToken cancellationToken = default)
    {
        // Create and return the task ID
        var task = _repository.Create(request.Title, request.Description);
        return ValueTask.FromResult(Result.Success(task.Id));
    }
}

[RequestHandler<CompleteTaskCommand>]
public sealed class CompleteTaskCommandHandler : IRequestHandler<CompleteTaskCommand>
{
    private readonly TaskRepository _repository;

    public CompleteTaskCommandHandler(TaskRepository repository)
    {
        _repository = repository;
    }

    public ValueTask<Result> HandleAsync(CompleteTaskCommand request, CancellationToken cancellationToken = default)
    {
        var task = _repository.GetById(request.TaskId);
        if (task == null)
            return ValueTask.FromResult(Result.Failure("Task not found"));

        task.Complete();
        return ValueTask.FromResult(Result.Success());
    }
}

// ═══════════════════════════════════════════════════════════════
// Query Handlers
// ═══════════════════════════════════════════════════════════════

/// <summary>
///     Handler for queries - read-only operations
/// </summary>
[RequestHandler<GetTaskQuery, TaskDto>]
public sealed class GetTaskQueryHandler : IRequestHandler<GetTaskQuery, TaskDto>
{
    private readonly TaskRepository _repository;

    public GetTaskQueryHandler(TaskRepository repository)
    {
        _repository = repository;
    }

    public ValueTask<Result<TaskDto>> HandleAsync(GetTaskQuery request, CancellationToken cancellationToken = default)
    {
        var task = _repository.GetById(request.TaskId);
        if (task == null)
            return ValueTask.FromResult(Result.Failure<TaskDto>("Task not found"));

        var dto = new TaskDto
        {
            Id = task.Id,
            Title = task.Title,
            Description = task.Description,
            Status = task.Status,
            CreatedAt = task.CreatedAt,
            CompletedAt = task.CompletedAt
        };

        return ValueTask.FromResult(Result.Success(dto));
    }
}

[RequestHandler<ListTasksQuery, List<TaskDto>>]
public sealed class ListTasksQueryHandler : IRequestHandler<ListTasksQuery, List<TaskDto>>
{
    private readonly TaskRepository _repository;

    public ListTasksQueryHandler(TaskRepository repository)
    {
        _repository = repository;
    }

    public ValueTask<Result<List<TaskDto>>> HandleAsync(ListTasksQuery request,
        CancellationToken cancellationToken = default)
    {
        var tasks = _repository.GetAll()
            .Select(t => new TaskDto
            {
                Id = t.Id,
                Title = t.Title,
                Description = t.Description,
                Status = t.Status,
                CreatedAt = t.CreatedAt,
                CompletedAt = t.CompletedAt
            })
            .ToList();

        return ValueTask.FromResult(Result.Success(tasks));
    }
}