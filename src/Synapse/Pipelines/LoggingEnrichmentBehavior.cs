using Microsoft.Extensions.Logging;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Pipelines;

/// <summary>
///     Pipeline behavior that enriches log context with request metadata and correlation information.
/// </summary>
/// <typeparam name="TRequest">The type of the request being processed.</typeparam>
/// <typeparam name="TResponse">The type of the response being returned.</typeparam>
public sealed class LoggingEnrichmentBehavior<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : notnull
{
    private readonly IContext _context;
    private readonly ILogger<LoggingEnrichmentBehavior<TRequest, TResponse>> _logger;

    /// <summary>
    ///     Initializes a new instance of the <see cref="LoggingEnrichmentBehavior{TRequest, TResponse}" /> class.
    /// </summary>
    /// <param name="context"></param>
    /// <param name="logger">The logger instance.</param>
    public LoggingEnrichmentBehavior(IContext context,
        ILogger<LoggingEnrichmentBehavior<TRequest, TResponse>> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    ///     Handles the request by enriching the logging scope with context metadata.
    /// </summary>
    /// <param name="request">The request being processed.</param>
    /// <param name="next">The next handler in the pipeline.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing the response.</returns>
    public ValueTask<Result<TResponse>> HandleAsync(TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        using (_logger.BeginScope(CreateState(_context)))
        {
            return next();
        }
    }

    private Dictionary<string, object> CreateState(IContext context)
    {
        var state = new Dictionary<string, object>
        {
            ["CorrelationId"] = context.CorrelationId
        };


        foreach (var metadata in context.Metadata) state[$"Metadata_{metadata.Key}"] = metadata.Value;

        return state;
    }
}