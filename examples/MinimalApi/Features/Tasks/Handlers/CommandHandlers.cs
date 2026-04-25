using UnambitiousFx.Examples.MinimalApi.Infrastructure;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.MinimalApi.Features.Tasks.Handlers;

// ═══════════════════════════════════════════════════════════════
// Command Handlers
// ═══════════════════════════════════════════════════════════════

[RequestHandler<CreateTaskCommand, Guid>]
public sealed class CreateTaskCommandHandler : IRequestHandler<CreateTaskCommand, Guid>
{
    private readonly IEmitter _emitter;
    private readonly ILogger<CreateTaskCommandHandler> _logger;
    private readonly TaskRepository _repository;

    public CreateTaskCommandHandler(
        TaskRepository repository,
        IEmitter emitter,
        ILogger<CreateTaskCommandHandler> logger)
    {
        _repository = repository;
        _emitter = emitter;
        _logger = logger;
    }

    public async ValueTask<Result<Guid>> HandleAsync(
        CreateTaskCommand request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating task: {Title}", request.Title);

        var task = _repository.Create(request.Title, request.Description);

        // Publish event
        await _emitter.EmitAsync(new TaskCreatedEvent
        {
            TaskId = task.Id,
            Title = task.Title,
            CreatedAt = task.CreatedAt
        }, cancellationToken);

        return Result.Success(task.Id);
    }
}

[RequestHandler<UpdateTaskCommand>]
public sealed class UpdateTaskCommandHandler : IRequestHandler<UpdateTaskCommand>
{
    private readonly ILogger<UpdateTaskCommandHandler> _logger;
    private readonly TaskRepository _repository;

    public UpdateTaskCommandHandler(
        TaskRepository repository,
        ILogger<UpdateTaskCommandHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public ValueTask<Result> HandleAsync(
        UpdateTaskCommand request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating task: {TaskId}", request.TaskId);

        var success = _repository.Update(request.TaskId, request.Title, request.Description);

        return success
            ? ValueTask.FromResult(Result.Success())
            : ValueTask.FromResult(Result.Failure($"Task {request.TaskId} not found"));
    }
}

[RequestHandler<CompleteTaskCommand>]
public sealed class CompleteTaskCommandHandler : IRequestHandler<CompleteTaskCommand>
{
    private readonly IEmitter _emitter;
    private readonly ILogger<CompleteTaskCommandHandler> _logger;
    private readonly TaskRepository _repository;

    public CompleteTaskCommandHandler(
        TaskRepository repository,
        IEmitter emitter,
        ILogger<CompleteTaskCommandHandler> logger)
    {
        _repository = repository;
        _emitter = emitter;
        _logger = logger;
    }

    public async ValueTask<Result> HandleAsync(
        CompleteTaskCommand request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Completing task: {TaskId}", request.TaskId);

        var task = _repository.GetById(request.TaskId);
        if (task == null)
        {
            return Result.Failure($"Task {request.TaskId} not found");
        }

        _repository.Complete(request.TaskId);

        // Publish event
        await _emitter.EmitAsync(new TaskCompletedEvent
        {
            TaskId = task.Id,
            Title = task.Title,
            CompletedAt = DateTime.UtcNow
        }, cancellationToken);

        return Result.Success();
    }
}

[RequestHandler<DeleteTaskCommand>]
public sealed class DeleteTaskCommandHandler : IRequestHandler<DeleteTaskCommand>
{
    private readonly IEmitter _emitter;
    private readonly ILogger<DeleteTaskCommandHandler> _logger;
    private readonly TaskRepository _repository;

    public DeleteTaskCommandHandler(
        TaskRepository repository,
        IEmitter emitter,
        ILogger<DeleteTaskCommandHandler> logger)
    {
        _repository = repository;
        _emitter = emitter;
        _logger = logger;
    }

    public async ValueTask<Result> HandleAsync(
        DeleteTaskCommand request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting task: {TaskId}", request.TaskId);

        var success = _repository.Delete(request.TaskId);

        if (!success)
        {
            return Result.Failure($"Task {request.TaskId} not found");
        }

        // Publish event
        await _emitter.EmitAsync(new TaskDeletedEvent
        {
            TaskId = request.TaskId,
            DeletedAt = DateTime.UtcNow
        }, cancellationToken);

        return Result.Success();
    }
}