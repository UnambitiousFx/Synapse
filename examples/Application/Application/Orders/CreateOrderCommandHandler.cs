using Microsoft.Extensions.Logging;
using UnambitiousFx.Examples.Application.Domain.Events;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.Application.Application.Orders;

[RequestHandler<CreateOrderCommand, Guid>]
public sealed class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, Guid>
{
    private readonly ILogger<CreateOrderCommandHandler> _logger;
    private readonly IPublisher _publisher;

    public CreateOrderCommandHandler(ILogger<CreateOrderCommandHandler> logger, IPublisher publisher)
    {
        _logger = logger;
        _publisher = publisher;
    }

    public async ValueTask<Result<Guid>> HandleAsync(CreateOrderCommand request,
        CancellationToken cancellationToken = default)
    {
        var orderId = Guid.NewGuid();

        _logger.LogInformation("Creating order {OrderId} for {CustomerName}", orderId, request.CustomerName);

        // Publish LOCAL event - handled within the same process
        await _publisher.PublishAsync(new OrderCreated
        {
            OrderId = orderId,
            CustomerName = request.CustomerName,
            TotalAmount = request.TotalAmount,
            CreatedAt = DateTime.UtcNow
        }, cancellationToken);

        // Also send a notification (local event)
        await _publisher.PublishAsync(new NotificationRequested
        {
            RecipientEmail = $"{request.CustomerName.ToLowerInvariant().Replace(" ", ".")}@example.com",
            Subject = "Order Confirmation",
            Message = $"Your order {orderId} has been created successfully!",
            RequestedAt = DateTime.UtcNow
        }, cancellationToken);

        return Result.Success(orderId);
    }
}