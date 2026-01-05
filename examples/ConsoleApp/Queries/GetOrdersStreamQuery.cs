using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.ConsoleApp.Queries;

/// <summary>
///     Streaming query that returns orders incrementally in batches.
///     This demonstrates efficient handling of large datasets without loading everything into memory.
/// </summary>
public sealed record GetOrdersStreamQuery : IStreamRequest<OrderDto>
{
    /// <summary>
    ///     Gets or initializes the page size for batching results.
    /// </summary>
    public int PageSize { get; init; } = 100;

    /// <summary>
    ///     Gets or initializes the total number of orders to generate.
    /// </summary>
    public int TotalOrders { get; init; } = 1000;
}