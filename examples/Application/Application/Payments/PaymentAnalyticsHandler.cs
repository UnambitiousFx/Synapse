using Microsoft.Extensions.Logging;
using UnambitiousFx.Examples.Application.Domain.Events;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.Application.Application.Payments;

/// <summary>
///     Second handler for PaymentProcessed event - demonstrates fan-out pattern
///     This handler tracks payment analytics
/// </summary>
[EventHandler<PaymentProcessed>]
public sealed class PaymentAnalyticsHandler : IEventHandler<PaymentProcessed>
{
    private readonly ILogger<PaymentAnalyticsHandler> _logger;

    public PaymentAnalyticsHandler(ILogger<PaymentAnalyticsHandler> logger)
    {
        _logger = logger;
    }

    public ValueTask<Result> HandleAsync(PaymentProcessed @event, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "📊 Recording payment analytics: {PaymentMethod} payment of {Amount:C}",
            @event.PaymentMethod,
            @event.Amount);

        // In a real application, this would update analytics database
        _logger.LogDebug("Analytics data recorded for payment {PaymentId}", @event.PaymentId);

        return ValueTask.FromResult(Result.Success());
    }
}