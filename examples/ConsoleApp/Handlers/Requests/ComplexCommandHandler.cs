using Microsoft.Extensions.Logging;
using UnambitiousFx.Examples.ConsoleApp.Commands;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.ConsoleApp.Handlers.Requests;

[RequestHandler<ComplexCommand, ComplexResponse>]
public sealed class ComplexCommandHandler : IRequestHandler<ComplexCommand, ComplexResponse>
{
    private readonly ILogger<ComplexCommandHandler> _logger;

    public ComplexCommandHandler(ILogger<ComplexCommandHandler> logger)
    {
        _logger = logger;
    }

    public ValueTask<Result<ComplexResponse>> HandleAsync(ComplexCommand request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Processing complex command with Step1: {Step1}, Step2: {Step2}", request.Step1,
            request.Step2);

        // Simulate some complex processing
        var response = new ComplexResponse
        {
            Result = $"Processed {request.Step1}",
            ProcessedCount = request.Step2
        };

        return ValueTask.FromResult(Result.Success(response));
    }
}