using Microsoft.Extensions.Logging;
using UnambitiousFx.Examples.Application.Domain.Events;
using UnambitiousFx.Examples.Application.Infrastructure.Services;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.Application.Application.Orders;

[EventHandler<OrderCancelled>]
public sealed class OrderCancelledHandler : IEventHandler<OrderCancelled>
{
    private readonly IFulfillmentService _fulfillmentService;
    private readonly ILogger<OrderCancelledHandler> _logger;

    public OrderCancelledHandler(
        ILogger<OrderCancelledHandler> logger,
        IFulfillmentService fulfillmentService)
    {
        _logger = logger;
        _fulfillmentService = fulfillmentService;
    }

    public ValueTask<Result> HandleAsync(
        OrderCancelled @event,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[EXTERNAL EVENT RECEIVED] Order {OrderId} cancelled: {Reason}",
            @event.OrderId, @event.Reason);

        // Cancel any pending fulfillment for this order
        _fulfillmentService.CancelFulfillment(@event.OrderId);
        _logger.LogInformation("Cancelled fulfillment for order {OrderId}", @event.OrderId);

        return ValueTask.FromResult(Result.Success());
    }
}