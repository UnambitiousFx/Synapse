using Microsoft.Extensions.Logging;
using UnambitiousFx.Examples.Application.Domain.Events;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.Application.Application.Orders;

[RequestHandler<ShipOrderCommand>]
public sealed class ShipOrderCommandHandler : IRequestHandler<ShipOrderCommand>
{
    private readonly ILogger<ShipOrderCommandHandler> _logger;
    private readonly IEmitter _emitter;

    public ShipOrderCommandHandler(ILogger<ShipOrderCommandHandler> logger, IEmitter emitter)
    {
        _logger = logger;
        _emitter = emitter;
    }

    public async ValueTask<Result> HandleAsync(ShipOrderCommand request, CancellationToken cancellationToken = default)
    {
        var trackingNumber = $"TRACK-{Guid.NewGuid():N}".Substring(0, 20).ToUpperInvariant();

        _logger.LogInformation("Shipping order {OrderId} with tracking {TrackingNumber}",
            request.OrderId, trackingNumber);

        await _emitter.EmitAsync(new OrderShipped
        {
            OrderId = request.OrderId,
            TrackingNumber = trackingNumber,
            ShippedAt = DateTime.UtcNow
        }, cancellationToken);

        return Result.Success();
    }
}