using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using UnambitiousFx.Examples.ConsoleApp.Queries;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.ConsoleApp.Handlers.Requests;

/// <summary>
///     Handler for streaming orders query.
///     Demonstrates how to implement efficient streaming of large datasets.
/// </summary>
[StreamRequestHandler<GetOrdersStreamQuery, OrderDto>]
public sealed class GetOrdersStreamQueryHandler : IStreamRequestHandler<GetOrdersStreamQuery, OrderDto>
{
    private readonly ILogger<GetOrdersStreamQueryHandler> _logger;

    public GetOrdersStreamQueryHandler(ILogger<GetOrdersStreamQueryHandler> logger)
    {
        _logger = logger;
    }

    public async IAsyncEnumerable<Result<OrderDto>> HandleAsync(GetOrdersStreamQuery request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting to stream {TotalOrders} orders with page size {PageSize}",
            request.TotalOrders,
            request.PageSize);

        var totalGenerated = 0;

        // Simulate streaming from database in batches
        for (var i = 0; i < request.TotalOrders; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Streaming cancelled after {Count} orders", totalGenerated);
                yield break;
            }

            var order = new OrderDto
            {
                Id = Guid.NewGuid(),
                ProductName = $"Product {i + 1}",
                Quantity = i % 10 + 1,
                TotalAmount = (i + 1) * 10.0m,
                CreatedAt = DateTime.UtcNow.AddDays(-i)
            };

            totalGenerated++;
            yield return Result.Success(order);

            // Simulate database delay every page
            if ((i + 1) % request.PageSize == 0)
            {
                _logger.LogDebug("Generated {Count} orders, fetching next batch...", totalGenerated);
                await Task.Delay(10, cancellationToken);
            }
        }

        _logger.LogInformation("Finished streaming {Count} orders", totalGenerated);
    }
}