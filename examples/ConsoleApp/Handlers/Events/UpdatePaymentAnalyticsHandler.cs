using Microsoft.Extensions.Logging;
using UnambitiousFx.Examples.ConsoleApp.Events;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.ConsoleApp.Handlers.Events;

[EventHandler<PaymentCompletedEvent>]
public class UpdatePaymentAnalyticsHandler : IEventHandler<PaymentCompletedEvent>
{
    private readonly ILogger<UpdatePaymentAnalyticsHandler> _logger;

    public UpdatePaymentAnalyticsHandler(ILogger<UpdatePaymentAnalyticsHandler> logger)
    {
        _logger = logger;
    }

    public ValueTask<Result> HandleAsync(PaymentCompletedEvent @event,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Updating analytics for payment: {PaymentId}, Amount: {Amount}", @event.PaymentId,
            @event.Amount);
        return ValueTask.FromResult(Result.Success());
    }
}