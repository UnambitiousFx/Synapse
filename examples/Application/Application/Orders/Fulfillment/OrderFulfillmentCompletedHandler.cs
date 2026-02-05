using Microsoft.Extensions.Logging;
using UnambitiousFx.Examples.Application.Domain.Events;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.Application.Application.Orders.Fulfillment;

[EventHandler<OrderFulfillmentCompleted>]
public sealed class OrderFulfillmentCompletedHandler : IEventHandler<OrderFulfillmentCompleted>
{
    private readonly ILogger<OrderFulfillmentCompletedHandler> _logger;

    public OrderFulfillmentCompletedHandler(ILogger<OrderFulfillmentCompletedHandler> logger)
    {
        _logger = logger;
    }

    public ValueTask<Result> HandleAsync(
        OrderFulfillmentCompleted @event,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[EXTERNAL EVENT RECEIVED] Fulfillment {FulfillmentId} completed for order {OrderId} at {WarehouseLocation}",
            @event.FulfillmentId, @event.OrderId, @event.WarehouseLocation);

        // In a real system, you would update the order status in the database
        _logger.LogInformation("Order {OrderId} status updated to 'Fulfilled'", @event.OrderId);

        return ValueTask.FromResult(Result.Success());
    }
}