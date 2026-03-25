using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.WebApi.Features.Tasks;

// ═══════════════════════════════════════════════════════════════
// Events - Domain events that represent something that happened
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Event raised when a task is created
/// </summary>
public sealed record TaskCreatedEvent : IEvent
{
    public required Guid TaskId { get; init; }
    public required string Title { get; init; }
    public required DateTime CreatedAt { get; init; }
}

/// <summary>
/// Event raised when a task is completed
/// </summary>
public sealed record TaskCompletedEvent : IEvent
{
    public required Guid TaskId { get; init; }
    public required string Title { get; init; }
    public required DateTime CompletedAt { get; init; }
}

/// <summary>
/// Event raised when a task is deleted
/// </summary>
public sealed record TaskDeletedEvent : IEvent
{
    public required Guid TaskId { get; init; }
    public required DateTime DeletedAt { get; init; }
}
