using Microsoft.Extensions.Logging;
using UnambitiousFx.Examples.Application.Domain.Events;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.Application.Application.Payments;

/// <summary>
///     Handler for external PaymentProcessed events received from transport
/// </summary>
[EventHandler<PaymentProcessed>]
public sealed class PaymentProcessedHandler : IEventHandler<PaymentProcessed>
{
    private readonly ILogger<PaymentProcessedHandler> _logger;

    public PaymentProcessedHandler(ILogger<PaymentProcessedHandler> logger)
    {
        _logger = logger;
    }

    public ValueTask<Result> HandleAsync(PaymentProcessed @event, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Payment {PaymentId} processed for order {OrderId}: {Amount:C} via {PaymentMethod}",
            @event.PaymentId,
            @event.OrderId,
            @event.Amount,
            @event.PaymentMethod);

        return ValueTask.FromResult(Result.Success());
    }
}