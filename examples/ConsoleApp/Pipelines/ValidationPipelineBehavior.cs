using Microsoft.Extensions.Logging;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.ConsoleApp.Pipelines;

public class ValidationPipelineBehavior : IRequestPipelineBehavior
{
    private readonly ILogger<ValidationPipelineBehavior> _logger;

    public ValidationPipelineBehavior(ILogger<ValidationPipelineBehavior> logger)
    {
        _logger = logger;
    }

    public async ValueTask<Result> HandleAsync<TRequest>(TRequest request,
        RequestHandlerDelegate next,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest
    {
        _logger.LogDebug("Validating request: {RequestName}", request.GetType()
            .Name);

        // Simulate validation work
        await Task.CompletedTask;

        return await next();
    }

    public async ValueTask<Result<TResponse>> HandleAsync<TRequest, TResponse>(TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>
        where TResponse : notnull
    {
        _logger.LogDebug("Validating request: {RequestName}", request.GetType()
            .Name);

        // Simulate validation work
        await Task.CompletedTask;

        return await next();
    }
}