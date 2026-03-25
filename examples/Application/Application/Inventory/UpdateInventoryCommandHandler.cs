using Microsoft.Extensions.Logging;
using UnambitiousFx.Examples.Application.Domain.Events;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.Application.Application.Inventory;

[RequestHandler<UpdateInventoryCommand>]
public sealed class UpdateInventoryCommandHandler : IRequestHandler<UpdateInventoryCommand>
{
    private readonly ILogger<UpdateInventoryCommandHandler> _logger;
    private readonly IEmitter _emitter;

    public UpdateInventoryCommandHandler(IEmitter emitter, ILogger<UpdateInventoryCommandHandler> logger)
    {
        _logger = logger;
        _emitter = emitter;
    }

    public async ValueTask<Result> HandleAsync(UpdateInventoryCommand request,
        CancellationToken cancellationToken = default)
    {
        // Simulate current inventory (in real app, would query from database)
        var currentQuantity = 100;
        var newQuantity = currentQuantity + request.QuantityChange;

        _logger.LogInformation("Updating inventory for product {ProductId}: {QuantityChange:+#;-#;0}",
            request.ProductId, request.QuantityChange);

        // Publish EXTERNAL event - will be sent through transport layer
        await _emitter.EmitAsync(new InventoryUpdated
        {
            ProductId = request.ProductId,
            QuantityChange = request.QuantityChange,
            NewQuantity = newQuantity,
            UpdatedAt = DateTime.UtcNow
        }, cancellationToken);

        return Result.Success();
    }
}