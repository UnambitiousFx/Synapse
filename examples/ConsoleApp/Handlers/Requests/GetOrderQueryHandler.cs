using Microsoft.Extensions.Logging;
using UnambitiousFx.Examples.ConsoleApp.Queries;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.ConsoleApp.Handlers.Requests;

[RequestHandler<GetOrderQuery, OrderDto>]
public sealed class GetOrderQueryHandler : IRequestHandler<GetOrderQuery, OrderDto>
{
    private readonly ILogger<GetOrderQueryHandler> _logger;

    public GetOrderQueryHandler(ILogger<GetOrderQueryHandler> logger)
    {
        _logger = logger;
    }

    public ValueTask<Result<OrderDto>> HandleAsync(GetOrderQuery request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Fetching order {OrderId}", request.OrderId);

        // Simulate fetching from a database
        var order = new OrderDto
        {
            Id = request.OrderId,
            ProductName = "Sample Product",
            Quantity = 1,
            TotalAmount = 99.99m,
            CreatedAt = DateTime.UtcNow
        };

        return ValueTask.FromResult(Result.Success(order));
    }
}