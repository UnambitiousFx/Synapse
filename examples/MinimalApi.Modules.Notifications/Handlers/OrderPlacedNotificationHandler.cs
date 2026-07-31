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
    private readonly IContext _context;
    private readonly NotificationLog _log;
    private readonly ILogger<OrderPlacedNotificationHandler> _logger;

    public OrderPlacedNotificationHandler(NotificationLog log,
        IContext context,
        ILogger<OrderPlacedNotificationHandler> logger)
    {
        _log = log;
        _context = context;
        _logger = logger;
    }

    public ValueTask<Result> HandleAsync(OrderPlacedEvent @event, CancellationToken cancellationToken = default)
    {
        // This is the "mail" end of the chain. The trace id is the one the caller sent as traceparent, so a
        // support question about this notification can be traced back to the click that caused it — and pasted
        // straight into the tracing backend.
        var entry = new NotificationEntry
        {
            OrderId = @event.OrderId,
            Product = @event.Product,
            Quantity = @event.Quantity,
            ReceivedAt = DateTime.UtcNow,
            TraceId = _context.TraceId,
            CausationId = _context.CausationId,
        };

        _log.Add(entry);

        _logger.LogInformation(
            "🔔 [Notifications] Order {OrderId} received: {Quantity}x {Product} (trace {TraceId})",
            @event.OrderId,
            @event.Quantity,
            @event.Product,
            _context.TraceId);

        return ValueTask.FromResult(Result.Success());
    }
}
