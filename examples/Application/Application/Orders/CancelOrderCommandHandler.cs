using Microsoft.Extensions.Logging;
using UnambitiousFx.Examples.Application.Domain.Events;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.Application.Application.Orders;

[RequestHandler<CancelOrderCommand>]
public sealed class CancelOrderCommandHandler : IRequestHandler<CancelOrderCommand>
{
    private readonly IEmitter _emitter;
    private readonly ILogger<CancelOrderCommandHandler> _logger;

    public CancelOrderCommandHandler(
        ILogger<CancelOrderCommandHandler> logger,
        IEmitter emitter)
    {
        _logger = logger;
        _emitter = emitter;
    }

    public async ValueTask<Result> HandleAsync(
        CancelOrderCommand request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Cancelling order {OrderId} with reason: {Reason}",
            request.OrderId, request.Reason);

        await _emitter.EmitAsync(new OrderCancelled
        {
            OrderId = request.OrderId,
            Reason = request.Reason,
            CancelledAt = DateTime.UtcNow
        }, cancellationToken);

        return Result.Success();
    }
}