using Microsoft.Extensions.Logging;
using UnambitiousFx.Examples.Application.Domain.Events;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.Application.Application.Payments;

[RequestHandler<ProcessPaymentCommand>]
public sealed class ProcessPaymentCommandHandler : IRequestHandler<ProcessPaymentCommand>
{
    private readonly ILogger<ProcessPaymentCommandHandler> _logger;
    private readonly IEmitter _emitter;

    public ProcessPaymentCommandHandler(ILogger<ProcessPaymentCommandHandler> logger, IEmitter emitter)
    {
        _logger = logger;
        _emitter = emitter;
    }

    public async ValueTask<Result> HandleAsync(ProcessPaymentCommand request,
        CancellationToken cancellationToken = default)
    {
        var paymentId = Guid.NewGuid();

        _logger.LogInformation("Processing payment {PaymentId} for order {OrderId}", paymentId, request.OrderId);

        await _emitter.EmitAsync(new PaymentProcessed
        {
            PaymentId = paymentId,
            OrderId = request.OrderId,
            Amount = request.Amount,
            PaymentMethod = request.PaymentMethod,
            ProcessedAt = DateTime.UtcNow
        }, cancellationToken);

        return Result.Success();
    }
}