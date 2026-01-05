using Microsoft.Extensions.Logging;
using UnambitiousFx.Examples.Application.Domain.Events;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.Application.Application.Orders;

/// <summary>
///     Second handler for OrderShipped event - demonstrates fan-out pattern
///     Multiple handlers can process the same external event
/// </summary>
[EventHandler<OrderShipped>]
public sealed class OrderShippedNotificationHandler : IEventHandler<OrderShipped>
{
    private readonly ILogger<OrderShippedNotificationHandler> _logger;

    public OrderShippedNotificationHandler(ILogger<OrderShippedNotificationHandler> logger)
    {
        _logger = logger;
    }

    public ValueTask<Result> HandleAsync(OrderShipped @event, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "📧 Sending shipping notification for order {OrderId} with tracking {TrackingNumber}",
            @event.OrderId,
            @event.TrackingNumber);

        // In a real application, this would send an email/SMS to the customer
        _logger.LogDebug("Email sent to customer with tracking information");

        return ValueTask.FromResult(Result.Success());
    }
}