using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.Application.Domain.Events;

/// <summary>
///     Local event - handled by notification service within the same process
/// </summary>
public sealed record NotificationRequested : IEvent
{
    public required string RecipientEmail { get; init; }
    public required string Subject { get; init; }
    public required string Message { get; init; }
    public required DateTime RequestedAt { get; init; }
}