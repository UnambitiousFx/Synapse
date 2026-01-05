namespace UnambitiousFx.Examples.Application.Infrastructure.Services;

public interface IFulfillmentService
{
    void AddFulfillment(Guid fulfillmentId, Guid orderId, string warehouseLocation);
    FulfillmentInfo? GetFulfillment(Guid fulfillmentId);
    void CompleteFulfillment(Guid fulfillmentId);
    void CancelFulfillment(Guid orderId);
    IEnumerable<FulfillmentInfo> GetAllFulfillments();
}

public sealed record FulfillmentInfo
{
    public required Guid FulfillmentId { get; init; }
    public required Guid OrderId { get; init; }
    public required string WarehouseLocation { get; init; }
    public required string Status { get; init; } // Pending, Completed, Cancelled
    public required DateTime RequestedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
}