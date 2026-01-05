using Microsoft.Extensions.Logging;
using UnambitiousFx.Examples.ConsoleApp.Commands;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.ConsoleApp.Handlers.Requests;

[RequestHandler<CreateOrderCommand, OrderCreatedResponse>]
public sealed class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, OrderCreatedResponse>
{
    private readonly ILogger<CreateOrderCommandHandler> _logger;

    public CreateOrderCommandHandler(ILogger<CreateOrderCommandHandler> logger)
    {
        _logger = logger;
    }

    public ValueTask<Result<OrderCreatedResponse>> HandleAsync(CreateOrderCommand request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Creating order for {ProductName}", request.ProductName);

        var response = new OrderCreatedResponse
        {
            OrderId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            TotalAmount = request.Price * request.Quantity
        };

        return ValueTask.FromResult(Result.Success(response));
    }
}