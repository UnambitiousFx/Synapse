using Microsoft.Extensions.Logging;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.WebApi.Features.Tasks.Handlers;

// ═══════════════════════════════════════════════════════════════
// Event Handlers
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Logs when a task is created
/// </summary>
[EventHandler<TaskCreatedEvent>]
public sealed class TaskCreatedLoggingHandler : IEventHandler<TaskCreatedEvent>
{
    private readonly ILogger<TaskCreatedLoggingHandler> _logger;

    public TaskCreatedLoggingHandler(ILogger<TaskCreatedLoggingHandler> logger)
    {
        _logger = logger;
    }

    public ValueTask<Result> HandleAsync(
        TaskCreatedEvent @event,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "📝 Task created: {Title} (ID: {TaskId})",
            @event.Title,
            @event.TaskId);

        return ValueTask.FromResult(Result.Success());
    }
}

/// <summary>
/// Logs when a task is completed
/// </summary>
[EventHandler<TaskCompletedEvent>]
public sealed class TaskCompletedLoggingHandler : IEventHandler<TaskCompletedEvent>
{
    private readonly ILogger<TaskCompletedLoggingHandler> _logger;

    public TaskCompletedLoggingHandler(ILogger<TaskCompletedLoggingHandler> logger)
    {
        _logger = logger;
    }

    public ValueTask<Result> HandleAsync(
        TaskCompletedEvent @event,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "✅ Task completed: {Title} (ID: {TaskId})",
            @event.Title,
            @event.TaskId);

        return ValueTask.FromResult(Result.Success());
    }
}

/// <summary>
/// Sends notifications when a task is completed (simulated)
/// </summary>
[EventHandler<TaskCompletedEvent>]
public sealed class TaskCompletedNotificationHandler : IEventHandler<TaskCompletedEvent>
{
    private readonly ILogger<TaskCompletedNotificationHandler> _logger;

    public TaskCompletedNotificationHandler(ILogger<TaskCompletedNotificationHandler> logger)
    {
        _logger = logger;
    }

    public ValueTask<Result> HandleAsync(
        TaskCompletedEvent @event,
        CancellationToken cancellationToken = default)
    {
        // In a real application, this would send an email, push notification, etc.
        _logger.LogInformation(
            "📧 Sending notification for completed task: {Title}",
            @event.Title);

        return ValueTask.FromResult(Result.Success());
    }
}

/// <summary>
/// Logs when a task is deleted
/// </summary>
[EventHandler<TaskDeletedEvent>]
public sealed class TaskDeletedLoggingHandler : IEventHandler<TaskDeletedEvent>
{
    private readonly ILogger<TaskDeletedLoggingHandler> _logger;

    public TaskDeletedLoggingHandler(ILogger<TaskDeletedLoggingHandler> logger)
    {
        _logger = logger;
    }

    public ValueTask<Result> HandleAsync(
        TaskDeletedEvent @event,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "🗑️ Task deleted: {TaskId}",
            @event.TaskId);

        return ValueTask.FromResult(Result.Success());
    }
}
