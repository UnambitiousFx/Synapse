using Microsoft.Extensions.Logging;
using UnambitiousFx.Examples.Application.Domain.Events;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.Application.Application.Todos;

public sealed class TodoCreatedHandler : IEventHandler<TodoCreated>
{
    private readonly ILogger<TodoCreatedHandler> _logger;

    public TodoCreatedHandler(ILogger<TodoCreatedHandler> logger)
    {
        _logger = logger;
    }

    public ValueTask<Result> HandleAsync(TodoCreated @event,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("New todo created {TodoId}", @event.Todo.Id);

        return ValueTask.FromResult(Result.Success());
    }
}