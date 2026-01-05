using Microsoft.Extensions.Logging;
using UnambitiousFx.Examples.Application.Domain.Events;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.Application.Application.Notifications;

/// <summary>
///     Local handler - sends notifications within the same process
/// </summary>
[EventHandler<NotificationRequested>]
public sealed class NotificationRequestedHandler : IEventHandler<NotificationRequested>
{
    private readonly ILogger<NotificationRequestedHandler> _logger;

    public NotificationRequestedHandler(ILogger<NotificationRequestedHandler> logger)
    {
        _logger = logger;
    }

    public ValueTask<Result> HandleAsync(NotificationRequested @event, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Sending notification to {RecipientEmail}: {Subject}",
            @event.RecipientEmail,
            @event.Subject);

        // Simulate sending email
        _logger.LogDebug("Email content: {Message}", @event.Message);

        return ValueTask.FromResult(Result.Success());
    }
}