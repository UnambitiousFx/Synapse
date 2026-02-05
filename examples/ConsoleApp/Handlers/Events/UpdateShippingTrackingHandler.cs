using Microsoft.Extensions.Logging;
using UnambitiousFx.Examples.ConsoleApp.Events;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.ConsoleApp.Handlers.Events;

[EventHandler<OrderShippedEvent>]
public sealed class UpdateShippingTrackingHandler : IEventHandler<OrderShippedEvent>
{
    private readonly ILogger<UpdateShippingTrackingHandler> _logger;

    public UpdateShippingTrackingHandler(ILogger<UpdateShippingTrackingHandler> logger)
    {
        _logger = logger;
    }

    public ValueTask<Result> HandleAsync(OrderShippedEvent @event,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Updating tracking for order: {OrderId}, Shipping: {ShippingId}", @event.OrderId,
            @event.ShippingId);
        return ValueTask.FromResult(Result.Success());
    }
}