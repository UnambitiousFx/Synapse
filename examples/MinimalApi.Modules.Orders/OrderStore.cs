using System.Collections.Concurrent;

namespace UnambitiousFx.Examples.MinimalApi.Modules.Orders;

/// <summary>
///     Thread-safe in-memory store for placed orders. Registered as a singleton.
/// </summary>
public sealed class OrderStore
{
    private readonly ConcurrentDictionary<Guid, string> _orders = new();

    /// <summary>Records a placed order and returns its generated ID.</summary>
    public Guid Place(string product)
    {
        var id = Guid.NewGuid();
        _orders[id] = product;
        return id;
    }
}
