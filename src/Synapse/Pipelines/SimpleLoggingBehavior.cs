using System.Diagnostics;
using Microsoft.Extensions.Logging;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Pipelines;

/// <summary>
///     Logging behavior for requests that do not produce a response.
///     Logs timing and success/failure for each request of type <typeparamref name="TRequest" />.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
public sealed class SimpleLoggingBehavior<TRequest> : IRequestPipelineBehavior<TRequest>
    where TRequest : IRequest
{
    private readonly ILogger<SimpleLoggingBehavior<TRequest>> _logger;

    /// <summary>
    ///     Initializes a new instance of <see cref="SimpleLoggingBehavior{TRequest}" />.
    /// </summary>
    public SimpleLoggingBehavior(ILogger<SimpleLoggingBehavior<TRequest>> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<Result> HandleAsync(TRequest request,
        RequestHandlerDelegate<TRequest> next,
        CancellationToken cancellationToken = default)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var requestName = typeof(TRequest).Name;

        var result = await next(request, cancellationToken);

        var elapsedTime = Stopwatch.GetElapsedTime(startedAt);
        if (result.TryGetFailure(out var errors))
        {
            _logger.LogWarning("Request {RequestName} handled in {ElapsedTime} with error {ErrorMessage}",
                requestName, elapsedTime, errors.ToString());
        }
        else
        {
            _logger.LogInformation("Request {RequestName} handled in {ElapsedTime}", requestName, elapsedTime);
        }

        return result;
    }
}

/// <summary>
///     Logging behavior for requests that produce a response.
///     Logs timing and success/failure for each request of type <typeparamref name="TRequest" />.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public sealed class SimpleLoggingBehavior<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : notnull
{
    private readonly ILogger<SimpleLoggingBehavior<TRequest, TResponse>> _logger;

    /// <summary>
    ///     Initializes a new instance of <see cref="SimpleLoggingBehavior{TRequest, TResponse}" />.
    /// </summary>
    public SimpleLoggingBehavior(ILogger<SimpleLoggingBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<Result<TResponse>> HandleAsync(TRequest request,
        RequestHandlerDelegate<TRequest, TResponse> next,
        CancellationToken cancellationToken = default)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var requestName = typeof(TRequest).Name;

        var result = await next(request, cancellationToken);

        var elapsedTime = Stopwatch.GetElapsedTime(startedAt);
        if (!result.TryGet(out _, out var error))
        {
            _logger.LogWarning("Request {RequestName} handled in {ElapsedTime} with error {ErrorMessage}",
                requestName, elapsedTime, error.ToString());
        }
        else
        {
            _logger.LogInformation("Request {RequestName} handled in {ElapsedTime}", requestName, elapsedTime);
        }

        return result;
    }
}

/// <summary>
///     Logging behavior for events.
///     Logs timing and success/failure for each event of type <typeparamref name="TEvent" />.
/// </summary>
/// <typeparam name="TEvent">The event type.</typeparam>
public sealed class SimpleLoggingEventBehavior<TEvent> : IEventPipelineBehavior<TEvent>
    where TEvent : IEvent
{
    private readonly ILogger<SimpleLoggingEventBehavior<TEvent>> _logger;

    /// <summary>
    ///     Initializes a new instance of <see cref="SimpleLoggingEventBehavior{TEvent}" />.
    /// </summary>
    public SimpleLoggingEventBehavior(ILogger<SimpleLoggingEventBehavior<TEvent>> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<Result> HandleAsync(TEvent @event,
        EventHandlerDelegate<TEvent> next,
        CancellationToken cancellationToken = default)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var eventName = typeof(TEvent).Name;

        var result = await next(@event, cancellationToken);

        var elapsedTime = Stopwatch.GetElapsedTime(startedAt);
        if (result.TryGetFailure(out var error))
        {
            _logger.LogWarning("Event {EventName} handled in {ElapsedTime} with error {ErrorMessage}",
                eventName, elapsedTime, error.ToString());
        }
        else
        {
            _logger.LogInformation("Event {EventName} handled in {ElapsedTime}", eventName, elapsedTime);
        }

        return result;
    }
}
