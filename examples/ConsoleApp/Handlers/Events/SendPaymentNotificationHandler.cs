using Microsoft.Extensions.Logging;
using UnambitiousFx.Examples.ConsoleApp.Events;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.ConsoleApp.Handlers.Events;

[EventHandler<PaymentCompletedEvent>]
public class SendPaymentNotificationHandler : IEventHandler<PaymentCompletedEvent>
{
    private readonly ILogger<SendPaymentNotificationHandler> _logger;

    public SendPaymentNotificationHandler(ILogger<SendPaymentNotificationHandler> logger)
    {
        _logger = logger;
    }

    public ValueTask<Result> HandleAsync(PaymentCompletedEvent @event,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Sending notification for payment: {PaymentId}", @event.PaymentId);
        return ValueTask.FromResult(Result.Success());
    }
}