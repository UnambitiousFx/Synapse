using System.Collections.Concurrent;

namespace UnambitiousFx.Examples.Application.Infrastructure.Services;

public sealed class InMemoryFulfillmentService : IFulfillmentService
{
    private readonly ConcurrentDictionary<Guid, FulfillmentInfo> _fulfillments = new();

    public void AddFulfillment(Guid fulfillmentId, Guid orderId, string warehouseLocation)
    {
        _fulfillments[fulfillmentId] = new FulfillmentInfo
        {
            FulfillmentId = fulfillmentId,
            OrderId = orderId,
            WarehouseLocation = warehouseLocation,
            Status = "Pending",
            RequestedAt = DateTime.UtcNow
        };
    }

    public FulfillmentInfo? GetFulfillment(Guid fulfillmentId)
    {
        return _fulfillments.TryGetValue(fulfillmentId, out var fulfillment) ? fulfillment : null;
    }

    public void CompleteFulfillment(Guid fulfillmentId)
    {
        if (_fulfillments.TryGetValue(fulfillmentId, out var fulfillment))
            _fulfillments[fulfillmentId] = fulfillment with
            {
                Status = "Completed",
                CompletedAt = DateTime.UtcNow
            };
    }

    public void CancelFulfillment(Guid orderId)
    {
        var fulfillment = _fulfillments.Values.FirstOrDefault(f => f.OrderId == orderId && f.Status == "Pending");
        if (fulfillment != null)
            _fulfillments[fulfillment.FulfillmentId] = fulfillment with
            {
                Status = "Cancelled"
            };
    }

    public IEnumerable<FulfillmentInfo> GetAllFulfillments()
    {
        return _fulfillments.Values.OrderByDescending(f => f.RequestedAt);
    }
}