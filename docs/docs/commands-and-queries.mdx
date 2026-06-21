---
sidebar_position: 3
---

# Commands and Queries

In Synapse every operation that expects a response is modelled as a **request**. Commands and queries are both requests — the difference is a matter of intent and convention, not a technical distinction enforced by the framework.

| Concept     | Intent                                             | Marker interface                    |
| ----------- | -------------------------------------------------- | ----------------------------------- |
| **Command** | Mutates state, may return an ID or acknowledgement | `IRequest<TResponse>` or `IRequest` |
| **Query**   | Reads state, always returns data                   | `IRequest<TResponse>`               |

For strict command/query separation at runtime, see [Pipeline Behaviors — CQRS enforcement](./pipelines#cqrs-boundary-enforcement).

## Requests with a response — `IRequest<TResponse>`

Use `IRequest<TResponse>` when the operation produces a typed result:

```csharp
public record GetTaskQuery(Guid TaskId) : IRequest<TaskDto>;
public record CreateTaskCommand(string Title) : IRequest<Guid>;
```

The handler interface is `IRequestHandler<TRequest, TResponse>`:

```csharp
public class GetTaskQueryHandler : IRequestHandler<GetTaskQuery, TaskDto>
{
    private readonly ITaskRepository _repository;

    public GetTaskQueryHandler(ITaskRepository repository) => _repository = repository;

    public async ValueTask<Result<TaskDto>> HandleAsync(
        GetTaskQuery query,
        CancellationToken ct = default)
    {
        var task = await _repository.FindAsync(query.TaskId, ct);

        return task is null
            ? Result.Failure<TaskDto>($"Task {query.TaskId} not found.")
            : Result.Success(TaskDto.From(task));
    }
}
```

## Fire-and-forget commands — `IRequest`

Use `IRequest` (without type parameter) when the command produces no value — only success or failure:

```csharp
public record DeleteTaskCommand(Guid TaskId) : IRequest;

public class DeleteTaskCommandHandler : IRequestHandler<DeleteTaskCommand>
{
    private readonly ITaskRepository _repository;

    public DeleteTaskCommandHandler(ITaskRepository repository) => _repository = repository;

    public async ValueTask<Result> HandleAsync(
        DeleteTaskCommand command,
        CancellationToken ct = default)
    {
        var deleted = await _repository.DeleteAsync(command.TaskId, ct);

        return deleted
            ? Result.Success()
            : Result.Failure($"Task {command.TaskId} not found.");
    }
}
```

## Optional base class

Both handler interfaces have an optional base class that adapts a synchronous `Handle` method to the async interface. Use it only when your handler has no async work:

```csharp
public class EchoQueryHandler : RequestHandler<EchoQuery, string>
{
    protected override Result<string> Handle(EchoQuery query)
        => Result.Success(query.Message);
}
```

## Invoking requests

Inject `IInvoker` and call `InvokeAsync`. The response type is inferred from the `IRequest<TResponse>` type argument — no need to specify it explicitly:

```csharp
// Typed response — TResponse inferred as TaskDto
Result<TaskDto> result = await invoker.InvokeAsync(new GetTaskQuery(id), ct);

// Typed response — TResponse inferred as Guid
Result<Guid> created = await invoker.InvokeAsync(new CreateTaskCommand("Buy milk"), ct);

// No response
Result deleted = await invoker.InvokeAsync(new DeleteTaskCommand(id), ct);
```

## Registration

Register each handler in `AddSynapse`:

```csharp
services.AddSynapse(cfg =>
{
    // With response
    cfg.RegisterRequestHandler<GetTaskQueryHandler, GetTaskQuery, TaskDto>();
    cfg.RegisterRequestHandler<CreateTaskHandler, CreateTaskCommand, Guid>();

    // Without response
    cfg.RegisterRequestHandler<DeleteTaskHandler, DeleteTaskCommand>();

    // Conditional — only registered when the predicate returns true
    cfg.RegisterRequestHandlerWhen<FeatureFlagHandler, MyCommand, bool>(() => featureEnabled);
});
```

Each handler type is stored in an O(1) dispatch dictionary keyed on the request type, looked up once per `InvokeAsync` call.

## Modular registration with `IRegisterGroup`

For larger projects, group related registrations into a class that implements `IRegisterGroup`:

```csharp
public class TasksRegisterGroup : IRegisterGroup
{
    public void Register(IDependencyInjectionBuilder builder)
    {
        builder.RegisterRequestHandler<GetTaskQueryHandler, GetTaskQuery, TaskDto>();
        builder.RegisterRequestHandler<CreateTaskHandler, CreateTaskCommand, Guid>();
        builder.RegisterRequestHandler<DeleteTaskHandler, DeleteTaskCommand>();
    }
}

// In Program.cs
services.AddSynapse(cfg => cfg.AddRegisterGroup(new TasksRegisterGroup()));
```

The [Source Generator](./source-generator) can generate this class automatically from handler attributes.

## See also

- [Validation](./validation) — run validators before the handler is invoked.
- [Pipeline Behaviors](./pipelines) — add cross-cutting concerns to all or specific request types.
- [Error Handling](./error-handling) — work with `Result` and `Result<T>` in call sites.
