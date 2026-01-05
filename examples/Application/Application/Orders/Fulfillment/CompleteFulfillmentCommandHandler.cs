using Microsoft.Extensions.Logging;
using UnambitiousFx.Examples.Application.Domain.Events;
using UnambitiousFx.Examples.Application.Infrastructure.Services;
using UnambitiousFx.Functional;
using UnambitiousFx.Functional.Errors;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.Application.Application.Orders.Fulfillment;

[RequestHandler<CompleteFulfillmentCommand>]
public sealed class CompleteFulfillmentCommandHandler : IRequestHandler<CompleteFulfillmentCommand>
{
    private readonly IFulfillmentService _fulfillmentService;
    private readonly ILogger<CompleteFulfillmentCommandHandler> _logger;
    private readonly IPublisher _publisher;

    public CompleteFulfillmentCommandHandler(
        ILogger<CompleteFulfillmentCommandHandler> logger,
        IPublisher publisher,
        IFulfillmentService fulfillmentService)
    {
        _logger = logger;
        _publisher = publisher;
        _fulfillmentService = fulfillmentService;
    }

    public async ValueTask<Result> HandleAsync(
        CompleteFulfillmentCommand request,
        CancellationToken cancellationToken = default)
    {
        var fulfillment = _fulfillmentService.GetFulfillment(request.FulfillmentId);
        if (fulfillment == null)
            return Result.Failure(new NotFoundError(nameof(FulfillmentInfo), request.FulfillmentId.ToString()));

        if (fulfillment.Status == "Completed")
            return Result.Failure(new ValidationError([$"Fulfillment {request.FulfillmentId} is already completed"]));

        if (fulfillment.Status == "Cancelled")
            return Result.Failure(new ValidationError([$"Fulfillment {request.FulfillmentId} is cancelled"]));

        _fulfillmentService.CompleteFulfillment(request.FulfillmentId);

        _logger.LogInformation(
            "Completing fulfillment {FulfillmentId} for order {OrderId}",
            request.FulfillmentId, fulfillment.OrderId);

        // Publish EXTERNAL event
        await _publisher.PublishAsync(new OrderFulfillmentCompleted
        {
            OrderId = fulfillment.OrderId,
            FulfillmentId = request.FulfillmentId,
            WarehouseLocation = fulfillment.WarehouseLocation,
            CompletedAt = DateTime.UtcNow
        }, cancellationToken);

        return Result.Success();
    }
}