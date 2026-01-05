using Microsoft.Extensions.Logging;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.ConsoleApp.Pipelines;

public class EventLoggingPipelineBehavior : IEventPipelineBehavior
{
    private readonly ILogger<EventLoggingPipelineBehavior> _logger;

    public EventLoggingPipelineBehavior(ILogger<EventLoggingPipelineBehavior> logger)
    {
        _logger = logger;
    }

    public async ValueTask<Result> HandleAsync<TEvent>(TEvent @event,
        EventHandlerDelegate next,
        CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        _logger.LogDebug("Processing event: {EventName}", @event.GetType()
            .Name);

        var result = await next();

        _logger.LogDebug("Processed event: {EventName}", @event.GetType()
            .Name);
        return result;
    }
}