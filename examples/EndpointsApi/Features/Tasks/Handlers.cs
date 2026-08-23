using System.Runtime.CompilerServices;
using UnambitiousFx.Examples.EndpointsApi.Infrastructure;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.EndpointsApi.Features.Tasks;

/// <summary>Handles <see cref="ListTasksQuery" /> by returning every task in the repository.</summary>
[RequestHandler<ListTasksQuery, IReadOnlyList<TaskDto>>]
public sealed class ListTasksQueryHandler : IRequestHandler<ListTasksQuery, IReadOnlyList<TaskDto>>
{
    private readonly TaskRepository _repository;

    /// <summary>Initializes a new instance of the <see cref="ListTasksQueryHandler" /> class.</summary>
    public ListTasksQueryHandler(TaskRepository repository)
    {
        _repository = repository;
    }

    /// <inheritdoc />
    public ValueTask<Result<IReadOnlyList<TaskDto>>> HandleAsync(
        ListTasksQuery request,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(Result.Success<IReadOnlyList<TaskDto>>(_repository.GetAll()));
    }
}

/// <summary>Handles <see cref="GetTaskQuery" /> by looking up one task by id.</summary>
[RequestHandler<GetTaskQuery, TaskDto>]
public sealed class GetTaskQueryHandler : IRequestHandler<GetTaskQuery, TaskDto>
{
    private readonly TaskRepository _repository;

    /// <summary>Initializes a new instance of the <see cref="GetTaskQueryHandler" /> class.</summary>
    public GetTaskQueryHandler(TaskRepository repository)
    {
        _repository = repository;
    }

    /// <inheritdoc />
    public ValueTask<Result<TaskDto>> HandleAsync(
        GetTaskQuery request,
        CancellationToken cancellationToken = default)
    {
        var task = _repository.GetById(request.TaskId);
        return ValueTask.FromResult(task is not null
            ? Result.Success(task)
            : Result.FailNotFound<TaskDto>("Task", request.TaskId.ToString()));
    }
}

/// <summary>Handles <see cref="StreamTasksQuery" /> by yielding every task one at a time.</summary>
[StreamRequestHandler<StreamTasksQuery, TaskDto>]
public sealed class StreamTasksQueryHandler : IStreamRequestHandler<StreamTasksQuery, TaskDto>
{
    private readonly TaskRepository _repository;

    /// <summary>Initializes a new instance of the <see cref="StreamTasksQueryHandler" /> class.</summary>
    public StreamTasksQueryHandler(TaskRepository repository)
    {
        _repository = repository;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<Result<TaskDto>> HandleAsync(
        StreamTasksQuery request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var task in _repository.GetAll())
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return Result.Success(task);
            await Task.Yield();
        }
    }
}

/// <summary>Handles <see cref="CreateTaskCommand" /> by adding a new task to the repository.</summary>
[RequestHandler<CreateTaskCommand, TaskCreated>]
public sealed class CreateTaskCommandHandler : IRequestHandler<CreateTaskCommand, TaskCreated>
{
    private readonly TaskRepository _repository;

    /// <summary>Initializes a new instance of the <see cref="CreateTaskCommandHandler" /> class.</summary>
    public CreateTaskCommandHandler(TaskRepository repository)
    {
        _repository = repository;
    }

    /// <inheritdoc />
    public ValueTask<Result<TaskCreated>> HandleAsync(
        CreateTaskCommand request,
        CancellationToken cancellationToken = default)
    {
        var task = _repository.Create(request.Title);
        return ValueTask.FromResult(Result.Success(new TaskCreated { TaskId = task.Id }));
    }
}

/// <summary>Handles <see cref="UpdateTaskCommand" /> by changing an existing task's title.</summary>
[RequestHandler<UpdateTaskCommand>]
public sealed class UpdateTaskCommandHandler : IRequestHandler<UpdateTaskCommand>
{
    private readonly TaskRepository _repository;

    /// <summary>Initializes a new instance of the <see cref="UpdateTaskCommandHandler" /> class.</summary>
    public UpdateTaskCommandHandler(TaskRepository repository)
    {
        _repository = repository;
    }

    /// <inheritdoc />
    public ValueTask<Result> HandleAsync(
        UpdateTaskCommand request,
        CancellationToken cancellationToken = default)
    {
        var updated = _repository.Update(request.TaskId, request.Title);
        return ValueTask.FromResult(updated
            ? Result.Success()
            : Result.FailNotFound("Task", request.TaskId.ToString()));
    }
}

/// <summary>Handles <see cref="DeleteTaskCommand" /> by removing a task from the repository.</summary>
[RequestHandler<DeleteTaskCommand>]
public sealed class DeleteTaskCommandHandler : IRequestHandler<DeleteTaskCommand>
{
    private readonly TaskRepository _repository;

    /// <summary>Initializes a new instance of the <see cref="DeleteTaskCommandHandler" /> class.</summary>
    public DeleteTaskCommandHandler(TaskRepository repository)
    {
        _repository = repository;
    }

    /// <inheritdoc />
    public ValueTask<Result> HandleAsync(
        DeleteTaskCommand request,
        CancellationToken cancellationToken = default)
    {
        var deleted = _repository.Delete(request.TaskId);
        return ValueTask.FromResult(deleted
            ? Result.Success()
            : Result.FailNotFound("Task", request.TaskId.ToString()));
    }
}
