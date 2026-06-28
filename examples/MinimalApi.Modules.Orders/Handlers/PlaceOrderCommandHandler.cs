using Microsoft.Extensions.Logging;
using UnambitiousFx.Examples.MinimalApi.Modules.Contracts;
using UnambitiousFx.Examples.MinimalApi.Modules.Orders.Messages;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.MinimalApi.Modules.Orders.Handlers;

/// <summary>
///     Handles <see cref="PlaceOrderCommand" />: persists the order and publishes
///     <see cref="OrderPlacedEvent" /> so any subscriber (e.g. the Notifications module)
///     can react — without Orders knowing who is listening.
/// </summary>
/// <remarks>
///     The <c>[RequestHandler&lt;TRequest, TResponse&gt;]</c> attribute tells the Synapse source
///     generator to include this handler in the generated <c>RegisterGroup</c>. The host adds it
///     with <c>cfg.AddRegisterGroup(new RegisterGroup())</c> — no manual registration needed.
/// </remarks>
[RequestHandler<PlaceOrderCommand, Guid>]
public sealed class PlaceOrderCommandHandler : IRequestHandler<PlaceOrderCommand, Guid>
{
    private readonly OrderStore _store;
    private readonly IEmitter _emitter;
    private readonly ILogger<PlaceOrderCommandHandler> _logger;

    public PlaceOrderCommandHandler(OrderStore store, IEmitter emitter, ILogger<PlaceOrderCommandHandler> logger)
    {
        _store = store;
        _emitter = emitter;
        _logger = logger;
    }

    public async ValueTask<Result<Guid>> HandleAsync(PlaceOrderCommand request, CancellationToken cancellationToken = default)
    {
        var orderId = _store.Place(request.Product);

        _logger.LogInformation(
            "📦 Order {OrderId} placed: {Quantity}x {Product}",
            orderId,
            request.Quantity,
            request.Product);

        // Emit the event — the Orders module does not know who handles it.
        // The Notifications module subscribed by calling cfg.RegisterEventHandler<...>() in the host.
        await _emitter.EmitAsync(new OrderPlacedEvent
        {
            OrderId = orderId,
            Product = request.Product,
            Quantity = request.Quantity,
            PlacedAt = DateTime.UtcNow,
        }, cancellationToken);

        return Result.Success(orderId);
    }
}
