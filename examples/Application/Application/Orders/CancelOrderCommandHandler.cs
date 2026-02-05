using Microsoft.Extensions.Logging;
using UnambitiousFx.Examples.Application.Domain.Events;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.Application.Application.Orders;

[RequestHandler<CancelOrderCommand>]
public sealed class CancelOrderCommandHandler : IRequestHandler<CancelOrderCommand>
{
    private readonly ILogger<CancelOrderCommandHandler> _logger;
    private readonly IPublisher _publisher;

    public CancelOrderCommandHandler(
        ILogger<CancelOrderCommandHandler> logger,
        IPublisher publisher)
    {
        _logger = logger;
        _publisher = publisher;
    }

    public async ValueTask<Result> HandleAsync(
        CancelOrderCommand request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Cancelling order {OrderId} with reason: {Reason}",
            request.OrderId, request.Reason);

        // Publish EXTERNAL event - will be consumed by fulfillment system
        await _publisher.PublishAsync(new OrderCancelled
        {
            OrderId = request.OrderId,
            Reason = request.Reason,
            CancelledAt = DateTime.UtcNow
        }, cancellationToken);

        return Result.Success();
    }
}