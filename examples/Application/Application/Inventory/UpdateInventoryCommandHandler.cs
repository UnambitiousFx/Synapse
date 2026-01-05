using Microsoft.Extensions.Logging;
using UnambitiousFx.Examples.Application.Domain.Events;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.Application.Application.Inventory;

[RequestHandler<UpdateInventoryCommand>]
public sealed class UpdateInventoryCommandHandler : IRequestHandler<UpdateInventoryCommand>
{
    private readonly ILogger<UpdateInventoryCommandHandler> _logger;
    private readonly IPublisher _publisher;

    public UpdateInventoryCommandHandler(IPublisher publisher, ILogger<UpdateInventoryCommandHandler> logger)
    {
        _logger = logger;
        _publisher = publisher;
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
        await _publisher.PublishAsync(new InventoryUpdated
        {
            ProductId = request.ProductId,
            QuantityChange = request.QuantityChange,
            NewQuantity = newQuantity,
            UpdatedAt = DateTime.UtcNow
        }, cancellationToken);

        return Result.Success();
    }
}