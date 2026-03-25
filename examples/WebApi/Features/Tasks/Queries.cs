using UnambitiousFx.Examples.WebApi.Infrastructure;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.WebApi.Features.Tasks;

// ═══════════════════════════════════════════════════════════════
// Queries - Read-only operations
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Query to get a task by ID
/// </summary>
public sealed record GetTaskQuery : IRequest<TaskDto>
{
    public required Guid TaskId { get; init; }
}

/// <summary>
/// Query to list all tasks
/// </summary>
public sealed record ListTasksQuery : IRequest<List<TaskDto>>;

// ═══════════════════════════════════════════════════════════════
// DTOs - Data Transfer Objects
// ═══════════════════════════════════════════════════════════════

public sealed record TaskDto
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required Infrastructure.TaskStatus Status { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
}
