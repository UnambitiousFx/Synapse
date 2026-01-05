using Microsoft.Extensions.Logging;
using UnambitiousFx.Examples.ConsoleApp.Commands;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.ConsoleApp.Handlers.Requests;

[RequestHandler<ConcurrentCommand>]
public sealed class ConcurrentCommandHandler : IRequestHandler<ConcurrentCommand>
{
    private readonly ILogger<ConcurrentCommandHandler> _logger;

    public ConcurrentCommandHandler(ILogger<ConcurrentCommandHandler> logger)
    {
        _logger = logger;
    }

    public ValueTask<Result> HandleAsync(ConcurrentCommand request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Processing concurrent command Batch: {BatchId}, Item: {ItemId}", request.BatchId,
            request.ItemId);
        return ValueTask.FromResult(Result.Success());
    }
}