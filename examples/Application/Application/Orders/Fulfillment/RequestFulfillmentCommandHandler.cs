using Microsoft.Extensions.Logging;
using UnambitiousFx.Examples.Application.Domain.Events;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.Application.Application.Orders.Fulfillment;

[RequestHandler<RequestFulfillmentCommand, Guid>]
public sealed class RequestFulfillmentCommandHandler : IRequestHandler<RequestFulfillmentCommand, Guid>
{
    private readonly ILogger<RequestFulfillmentCommandHandler> _logger;
    private readonly IPublisher _publisher;

    public RequestFulfillmentCommandHandler(
        ILogger<RequestFulfillmentCommandHandler> logger,
        IPublisher publisher)
    {
        _logger = logger;
        _publisher = publisher;
    }

    public async ValueTask<Result<Guid>> HandleAsync(
        RequestFulfillmentCommand request,
        CancellationToken cancellationToken = default)
    {
        var fulfillmentId = Guid.NewGuid();

        _logger.LogInformation(
            "Requesting fulfillment {FulfillmentId} for order {OrderId} at warehouse {WarehouseLocation}",
            fulfillmentId, request.OrderId, request.WarehouseLocation);

        // Publish EXTERNAL event
        await _publisher.PublishAsync(new OrderFulfillmentRequested
        {
            OrderId = request.OrderId,
            FulfillmentId = fulfillmentId,
            WarehouseLocation = request.WarehouseLocation,
            RequestedAt = DateTime.UtcNow
        }, cancellationToken);

        return Result.Success(fulfillmentId);
    }
}