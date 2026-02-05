using Microsoft.Extensions.Logging;
using UnambitiousFx.Examples.Application.Domain.Events;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.Application.Application.Orders;

[RequestHandler<ShipOrderCommand>]
public sealed class ShipOrderCommandHandler : IRequestHandler<ShipOrderCommand>
{
    private readonly ILogger<ShipOrderCommandHandler> _logger;
    private readonly IPublisher _publisher;

    public ShipOrderCommandHandler(ILogger<ShipOrderCommandHandler> logger, IPublisher publisher)
    {
        _logger = logger;
        _publisher = publisher;
    }

    public async ValueTask<Result> HandleAsync(ShipOrderCommand request, CancellationToken cancellationToken = default)
    {
        var trackingNumber = $"TRACK-{Guid.NewGuid():N}".Substring(0, 20).ToUpperInvariant();

        _logger.LogInformation("Shipping order {OrderId} with tracking {TrackingNumber}",
            request.OrderId, trackingNumber);

        // Publish EXTERNAL event - will be sent through transport layer (RabbitMQ, etc.)
        await _publisher.PublishAsync(new OrderShipped
        {
            OrderId = request.OrderId,
            TrackingNumber = trackingNumber,
            ShippedAt = DateTime.UtcNow
        }, cancellationToken);

        return Result.Success();
    }
}