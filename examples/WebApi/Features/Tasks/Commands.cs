using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.WebApi.Features.Tasks;

// ═══════════════════════════════════════════════════════════════
// Commands - Operations that modify state
// ═══════════════════════════════════════════════════════════════

/// <summary>
///     Command to create a new task
/// </summary>
public sealed record CreateTaskCommand : IRequest<Guid>
{
    public required string Title { get; init; }
    public required string Description { get; init; }
}

/// <summary>
///     Command to update an existing task
/// </summary>
public sealed record UpdateTaskCommand : IRequest
{
    public required Guid TaskId { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
}

/// <summary>
///     Command to complete a task
/// </summary>
public sealed record CompleteTaskCommand : IRequest
{
    public required Guid TaskId { get; init; }
}

/// <summary>
///     Command to delete a task
/// </summary>
public sealed record DeleteTaskCommand : IRequest
{
    public required Guid TaskId { get; init; }
}