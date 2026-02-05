using Microsoft.Extensions.Logging;
using UnambitiousFx.Examples.Application.Domain.Events;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.Application.Application.Todos;

[EventHandler<TodoUpdated>]
public sealed class TodoUpdatedHandler : IEventHandler<TodoUpdated>
{
    private readonly ILogger<TodoUpdatedHandler> _logger;

    public TodoUpdatedHandler(ILogger<TodoUpdatedHandler> logger)
    {
        _logger = logger;
    }

    public ValueTask<Result> HandleAsync(TodoUpdated @event,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Todo updated {TodoId}", @event.Todo.Id);

        return ValueTask.FromResult(Result.Success());
    }
}