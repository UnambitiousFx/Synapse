using UnambitiousFx.Synapse.Abstractions;
using TaskStatus = UnambitiousFx.Examples.MinimalApi.Infrastructure.TaskStatus;

namespace UnambitiousFx.Examples.MinimalApi.Features.Tasks;

// ═══════════════════════════════════════════════════════════════
// Queries - Read-only operations
// ═══════════════════════════════════════════════════════════════

/// <summary>
///     Query to get a task by ID
/// </summary>
public sealed record GetTaskQuery : IRequest<TaskDto>
{
    public required Guid TaskId { get; init; }
}

/// <summary>
///     Query to list all tasks
/// </summary>
public sealed record ListTasksQuery : IRequest<List<TaskDto>>;

/// <summary>
///     Streaming query that yields tasks one by one as they become available.
///     Demonstrates <see cref="IStreamRequest{TResponse}" /> and <c>IInvoker.InvokeStreamAsync</c>.
/// </summary>
public sealed record StreamTasksQuery : IStreamRequest<TaskDto>;

// ═══════════════════════════════════════════════════════════════
// DTOs - Data Transfer Objects
// ═══════════════════════════════════════════════════════════════

public sealed record TaskDto
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required TaskStatus Status { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
}