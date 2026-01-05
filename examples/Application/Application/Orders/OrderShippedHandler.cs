using Microsoft.Extensions.Logging;
using UnambitiousFx.Examples.Application.Domain.Events;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.Application.Application.Orders;

/// <summary>
///     Handler for external OrderShipped events received from transport
/// </summary>
[EventHandler<OrderShipped>]
public sealed class OrderShippedHandler : IEventHandler<OrderShipped>
{
    private readonly ILogger<OrderShippedHandler> _logger;

    public OrderShippedHandler(ILogger<OrderShippedHandler> logger)
    {
        _logger = logger;
    }

    public ValueTask<Result> HandleAsync(OrderShipped @event, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Order {OrderId} shipped with tracking number {TrackingNumber} at {ShippedAt}",
            @event.OrderId,
            @event.TrackingNumber,
            @event.ShippedAt);

        // In a real application, this might update a database, send notifications, etc.
        return ValueTask.FromResult(Result.Success());
    }
}