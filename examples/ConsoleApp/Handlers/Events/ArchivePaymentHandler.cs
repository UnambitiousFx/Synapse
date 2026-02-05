using Microsoft.Extensions.Logging;
using UnambitiousFx.Examples.ConsoleApp.Events;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.ConsoleApp.Handlers.Events;

[EventHandler<PaymentCompletedEvent>]
public class ArchivePaymentHandler : IEventHandler<PaymentCompletedEvent>
{
    private readonly ILogger<ArchivePaymentHandler> _logger;

    public ArchivePaymentHandler(ILogger<ArchivePaymentHandler> logger)
    {
        _logger = logger;
    }

    public ValueTask<Result> HandleAsync(PaymentCompletedEvent @event,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Archiving payment: {PaymentId}", @event.PaymentId);
        return ValueTask.FromResult(Result.Success());
    }
}