using Microsoft.Extensions.Logging;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.ConsoleApp.Pipelines;

// Simple logging pipeline behavior for requests
public class LoggingPipelineBehavior : IRequestPipelineBehavior
{
    private readonly ILogger<LoggingPipelineBehavior> _logger;

    public LoggingPipelineBehavior(ILogger<LoggingPipelineBehavior> logger)
    {
        _logger = logger;
    }

    public async ValueTask<Result> HandleAsync<TRequest>(TRequest request,
        RequestHandlerDelegate next,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest
    {
        var requestName = request.GetType()
            .Name;
        _logger.LogDebug("Handling request: {RequestName}", requestName);

        var response = await next();

        _logger.LogDebug("Handled request: {RequestName}", requestName);
        return response;
    }

    public async ValueTask<Result<TResponse>> HandleAsync<TRequest, TResponse>(TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>
        where TResponse : notnull
    {
        var requestName = request.GetType()
            .Name;
        _logger.LogDebug("Handling request: {RequestName}", requestName);

        var response = await next();

        _logger.LogDebug("Handled request: {RequestName}", requestName);
        return response;
    }
}

// Validation pipeline behavior

// Event pipeline behavior