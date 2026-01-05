using System.Diagnostics;
using Microsoft.Extensions.Logging;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Pipelines;

/// <summary>
///     Represents a behavior for logging execution details in the mediator pipeline.
///     This class implements both <see cref="IRequestPipelineBehavior" /> and <see cref="IEventPipelineBehavior" />
///     to provide logging capabilities for request and event handling.
/// </summary>
public sealed class SimpleLoggingBehavior : IRequestPipelineBehavior, IEventPipelineBehavior
{
    private readonly ILogger<SimpleLoggingBehavior> _logger;

    /// Represents a simple logging behavior in a mediator pipeline.
    /// This class implements both IRequestPipelineBehavior and IEventPipelineBehavior
    /// to add logging capabilities for requests and events, respectively.
    public SimpleLoggingBehavior(ILogger<SimpleLoggingBehavior> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<Result> HandleAsync<TEvent>(TEvent @event,
        EventHandlerDelegate next,
        CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        var startedAt = Stopwatch.GetTimestamp();
        var eventName = typeof(TEvent).Name;

        var result = await next();


        var elapsedTime = Stopwatch.GetElapsedTime(startedAt);
        if (!result.TryGet(out var error))
            _logger.LogWarning("Event {EventName} handled in {ElapsedMilliseconds}ms with error {ErrorMessage}",
                eventName, elapsedTime, error.ToString());
        else
            _logger.LogInformation("Event {EventName} handled in {ElapsedMilliseconds}ms", eventName, elapsedTime);

        return result;
    }

    /// <inheritdoc />
    public async ValueTask<Result> HandleAsync<TRequest>(TRequest request,
        RequestHandlerDelegate next,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest
    {
        var startedAt = Stopwatch.GetTimestamp();
        var requestName = typeof(TRequest).Name;


        var result = await next();


        var elapsedTime = Stopwatch.GetElapsedTime(startedAt);
        if (!result.TryGet(out var errors))
            _logger.LogWarning("Request {RequestName} handled in {ElapsedMilliseconds}ms with error {ErrorMessage}",
                requestName, elapsedTime, errors.ToString());
        else
            _logger.LogInformation("Request {RequestName} handled in {ElapsedMilliseconds}ms", requestName,
                elapsedTime);

        return result;
    }

    /// <inheritdoc />
    public async ValueTask<Result<TResponse>> HandleAsync<TRequest, TResponse>(TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>
        where TResponse : notnull
    {
        var startedAt = Stopwatch.GetTimestamp();
        var requestName = typeof(TRequest).Name;


        var result = await next();

        var elapsedTime = Stopwatch.GetElapsedTime(startedAt);
        if (!result.TryGet(out _, out var error))
            _logger.LogWarning("Request {RequestName} handled in {ElapsedMilliseconds}ms with error {ErrorMessage}",
                requestName, elapsedTime, error.ToString());
        else
            _logger.LogInformation("Request {RequestName} handled in {ElapsedMilliseconds}ms", requestName,
                elapsedTime);

        return result;
    }
}