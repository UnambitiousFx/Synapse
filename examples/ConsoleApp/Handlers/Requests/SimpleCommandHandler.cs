using Microsoft.Extensions.Logging;
using UnambitiousFx.Examples.ConsoleApp.Commands;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.ConsoleApp.Handlers.Requests;

[RequestHandler<SimpleCommand>]
public sealed class SimpleCommandHandler : IRequestHandler<SimpleCommand>
{
    private readonly ILogger<SimpleCommandHandler> _logger;

    public SimpleCommandHandler(ILogger<SimpleCommandHandler> logger)
    {
        _logger = logger;
    }

    public ResultTask HandleAsync(SimpleCommand request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Processing simple command: {Message}", request.Message);
        // Simulate some minimal work
        return ValueTask.FromResult(Result.Success());
    }
}