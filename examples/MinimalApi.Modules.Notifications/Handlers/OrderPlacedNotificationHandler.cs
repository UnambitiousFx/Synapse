using Microsoft.Extensions.Logging;
using UnambitiousFx.Examples.MinimalApi.Modules.Contracts;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.MinimalApi.Modules.Notifications.Handlers;

/// <summary>
///     Handles <see cref="OrderPlacedEvent" /> by writing a notification entry to <see cref="NotificationLog" />.
/// </summary>
/// <remarks>
///     <para>
///         This handler carries <strong>no</strong> <c>[EventHandler&lt;T&gt;]</c> attribute — it is
///         registered manually in the host via:
///         <code>cfg.RegisterEventHandler&lt;OrderPlacedNotificationHandler, OrderPlacedEvent&gt;();</code>
///         That one call wires both the DI registration <em>and</em> the AOT-safe event dispatch delegate,
///         so no source generator or <c>IEventDispatcherRegistration</c> plumbing is needed.
///     </para>
///     <para>
///         Compare with <c>PlaceOrderCommandHandler</c> in the Orders module, which uses
///         <c>[RequestHandler&lt;TRequest, TResponse&gt;]</c> and the generated <c>RegisterGroup</c>.
///         Both approaches are fully AOT-safe; the choice is a matter of preference and team convention.
///     </para>
/// </remarks>
public sealed class OrderPlacedNotificationHandler : IEventHandler<OrderPlacedEvent>
{
    private readonly NotificationLog _log;
    private readonly ILogger<OrderPlacedNotificationHandler> _logger;

    public OrderPlacedNotificationHandler(NotificationLog log, ILogger<OrderPlacedNotificationHandler> logger)
    {
        _log = log;
        _logger = logger;
    }

    public ValueTask<Result> HandleAsync(OrderPlacedEvent @event, CancellationToken cancellationToken = default)
    {
        var entry = new NotificationEntry
        {
            OrderId = @event.OrderId,
            Product = @event.Product,
            Quantity = @event.Quantity,
            ReceivedAt = DateTime.UtcNow,
        };

        _log.Add(entry);

        _logger.LogInformation(
            "🔔 [Notifications] Order {OrderId} received: {Quantity}x {Product}",
            @event.OrderId,
            @event.Quantity,
            @event.Product);

        return ValueTask.FromResult(Result.Success());
    }
}
