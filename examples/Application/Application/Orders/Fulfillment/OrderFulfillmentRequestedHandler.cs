using Microsoft.Extensions.Logging;
using UnambitiousFx.Examples.Application.Domain.Events;
using UnambitiousFx.Examples.Application.Infrastructure.Services;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.Application.Application.Orders.Fulfillment;

[EventHandler<OrderFulfillmentRequested>]
public sealed class OrderFulfillmentRequestedHandler : IEventHandler<OrderFulfillmentRequested>
{
    private readonly IFulfillmentService _fulfillmentService;
    private readonly ILogger<OrderFulfillmentRequestedHandler> _logger;

    public OrderFulfillmentRequestedHandler(
        ILogger<OrderFulfillmentRequestedHandler> logger,
        IFulfillmentService fulfillmentService)
    {
        _logger = logger;
        _fulfillmentService = fulfillmentService;
    }

    public ValueTask<Result> HandleAsync(
        OrderFulfillmentRequested @event,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[EXTERNAL EVENT RECEIVED] Fulfillment {FulfillmentId} requested for order {OrderId} at {WarehouseLocation}",
            @event.FulfillmentId, @event.OrderId, @event.WarehouseLocation);

        _fulfillmentService.AddFulfillment(@event.FulfillmentId, @event.OrderId, @event.WarehouseLocation);

        return ValueTask.FromResult(Result.Success());
    }
}