using Microsoft.Extensions.Logging;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.GettingStarted.Step2_Events;

// ═══════════════════════════════════════════════════════════════
// Event Handlers - Multiple handlers can subscribe to the same event
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// First handler - sends notification
/// </summary>
[EventHandler<TaskCompletedEvent>]
public sealed class SendNotificationHandler : IEventHandler<TaskCompletedEvent>
{
    private readonly ILogger<SendNotificationHandler> _logger;

    public SendNotificationHandler(ILogger<SendNotificationHandler> logger)
    {
        _logger = logger;
    }

    public ValueTask<Result> HandleAsync(TaskCompletedEvent @event, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("  → Sending notification for task: {@TaskTitle}", @event.TaskTitle);
        return ValueTask.FromResult(Result.Success());
    }
}

/// <summary>
/// Second handler - updates analytics
/// </summary>
[EventHandler<TaskCompletedEvent>]
public sealed class UpdateAnalyticsHandler : IEventHandler<TaskCompletedEvent>
{
    private readonly ILogger<UpdateAnalyticsHandler> _logger;

    public UpdateAnalyticsHandler(ILogger<UpdateAnalyticsHandler> logger)
    {
        _logger = logger;
    }

    public ValueTask<Result> HandleAsync(TaskCompletedEvent @event, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("  → Updating analytics for task completion");
        return ValueTask.FromResult(Result.Success());
    }
}

/// <summary>
/// Third handler - archives the task
/// </summary>
[EventHandler<TaskCompletedEvent>]
public sealed class ArchiveTaskHandler : IEventHandler<TaskCompletedEvent>
{
    private readonly ILogger<ArchiveTaskHandler> _logger;

    public ArchiveTaskHandler(ILogger<ArchiveTaskHandler> logger)
    {
        _logger = logger;
    }

    public ValueTask<Result> HandleAsync(TaskCompletedEvent @event, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("  → Archiving completed task {@TaskId}", @event.TaskId);
        return ValueTask.FromResult(Result.Success());
    }
}
