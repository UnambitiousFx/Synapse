using UnambitiousFx.Examples.WebApi.Infrastructure;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.WebApi.Features.Tasks.Handlers;

// ═══════════════════════════════════════════════════════════════
// Query Handlers
// ═══════════════════════════════════════════════════════════════

[RequestHandler<GetTaskQuery, TaskDto>]
public sealed class GetTaskQueryHandler : IRequestHandler<GetTaskQuery, TaskDto>
{
    private readonly TaskRepository _repository;

    public GetTaskQueryHandler(TaskRepository repository)
    {
        _repository = repository;
    }

    public ValueTask<Result<TaskDto>> HandleAsync(
        GetTaskQuery request,
        CancellationToken cancellationToken = default)
    {
        var task = _repository.GetById(request.TaskId);

        if (task == null)
            return ValueTask.FromResult(Result.Failure<TaskDto>($"Task {request.TaskId} not found"));

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

    public ValueTask<Result<List<TaskDto>>> HandleAsync(
        ListTasksQuery request,
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
