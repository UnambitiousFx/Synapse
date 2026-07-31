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
    public async ValueTask<Result<TResponse>> HandleAsync(TRequest request,
        RequestHandlerDelegate<TRequest, TResponse> next,
        CancellationToken cancellationToken = default)
    {
        using (_logger.BeginScope(CreateState(_context)))
        {
            return await next(request, cancellationToken);
        }
    }

    private static Dictionary<string, object> CreateState(IContext context)
    {
        // TraceId is the W3C trace id, so this value matches what the tracing backend shows. A host that also
        // enables ActivityTrackingOptions.TraceId gets an equal value from ILogger itself; ours is additionally
        // present when no activity exists.
        var state = new Dictionary<string, object>
        {
            ["TraceId"] = context.TraceId,
            ["OccurredAt"] = context.OccurredAt
        };

        if (context.CausationId is { } causationId)
        {
            state["CausationId"] = causationId;
        }

        // Only baggage is surfaced: it is the context state that is explicitly meant to travel and be
        // observed. Features stay out of the log scope on purpose — they hold process-local state such as
        // the CQRS boundary marker, which has no business appearing in every log entry.
        foreach (var entry in context.Baggage)
        {
            state[$"Baggage_{entry.Key}"] = entry.Value;
        }

        return state;
    }
}