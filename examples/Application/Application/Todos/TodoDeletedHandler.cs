using Microsoft.Extensions.Logging;
using UnambitiousFx.Examples.Application.Domain.Events;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.Application.Application.Todos;

[EventHandler<TodoDeleted>]
public sealed class TodoDeletedHandler : IEventHandler<TodoDeleted>
{
    private readonly ILogger<TodoDeletedHandler> _logger;

    public TodoDeletedHandler(ILogger<TodoDeletedHandler> logger)
    {
        _logger = logger;
    }

    public ValueTask<Result> HandleAsync(TodoDeleted @event,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Todo deleted {TodoId}", @event.Todo.Id);

        return ValueTask.FromResult(Result.Success());
    }
}