using Microsoft.Extensions.Logging;
using UnambitiousFx.Examples.Application.Domain.Events;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.Application.Application.Orders.Fulfillment;

[RequestHandler<RequestFulfillmentCommand, Guid>]
public sealed class RequestFulfillmentCommandHandler : IRequestHandler<RequestFulfillmentCommand, Guid>
{
    private readonly ILogger<RequestFulfillmentCommandHandler> _logger;
    private readonly IEmitter _emitter;

    public RequestFulfillmentCommandHandler(
        ILogger<RequestFulfillmentCommandHandler> logger,
        IEmitter emitter)
    {
        _logger = logger;
        _emitter = emitter;
    }

    public async ValueTask<Result<Guid>> HandleAsync(
        RequestFulfillmentCommand request,
        CancellationToken cancellationToken = default)
    {
        var fulfillmentId = Guid.NewGuid();

        _logger.LogInformation(
            "Requesting fulfillment {FulfillmentId} for order {OrderId} at warehouse {WarehouseLocation}",
            fulfillmentId, request.OrderId, request.WarehouseLocation);

        await _emitter.EmitAsync(new OrderFulfillmentRequested
        {
            OrderId = request.OrderId,
            FulfillmentId = fulfillmentId,
            WarehouseLocation = request.WarehouseLocation,
            RequestedAt = DateTime.UtcNow
        }, cancellationToken);

        return Result.Success(fulfillmentId);
    }
}