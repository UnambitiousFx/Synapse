using Microsoft.Extensions.Logging;
using UnambitiousFx.Examples.ConsoleApp.Events;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.ConsoleApp.Handlers.Events;

[EventHandler<OrderShippedEvent>]
public sealed class SendShippingNotificationHandler : IEventHandler<OrderShippedEvent>
{
    private readonly ILogger<SendShippingNotificationHandler> _logger;

    public SendShippingNotificationHandler(ILogger<SendShippingNotificationHandler> logger)
    {
        _logger = logger;
    }

    public ValueTask<Result> HandleAsync(OrderShippedEvent @event,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Sending shipping notification for order: {OrderId}", @event.OrderId);
        return ValueTask.FromResult(Result.Success());
    }
}