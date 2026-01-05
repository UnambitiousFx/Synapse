using Microsoft.Extensions.Logging;
using UnambitiousFx.Examples.Application.Domain.Events;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.Application.Application.Inventory;

/// <summary>
///     Handler for external InventoryUpdated events received from transport
/// </summary>
[EventHandler<InventoryUpdated>]
public sealed class InventoryUpdatedHandler : IEventHandler<InventoryUpdated>
{
    private readonly ILogger<InventoryUpdatedHandler> _logger;

    public InventoryUpdatedHandler(ILogger<InventoryUpdatedHandler> logger)
    {
        _logger = logger;
    }

    public ValueTask<Result> HandleAsync(InventoryUpdated @event, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Inventory updated for product {ProductId}: {QuantityChange:+#;-#;0} (New quantity: {NewQuantity})",
            @event.ProductId,
            @event.QuantityChange,
            @event.NewQuantity);

        return ValueTask.FromResult(Result.Success());
    }
}