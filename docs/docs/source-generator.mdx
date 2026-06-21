---
sidebar_position: 12
---

# Source Generator

`UnambitiousFx.Synapse.Generator` is a Roslyn incremental source generator that reads your handler class attributes and emits two registration classes at compile time. This eliminates manual `RegisterRequestHandler` / `RegisterEventHandler` calls and makes the codebase NativeAOT-compatible by capturing dispatch delegates without reflection.

## Install

```bash
dotnet add package UnambitiousFx.Synapse.Generator
```

No further setup is needed — the generator runs automatically during the build.

## Mark your handlers

Annotate each handler class with the corresponding attribute:

### `[RequestHandler<TRequest, TResponse>]` — for `IRequest<TResponse>`

```csharp
using UnambitiousFx.Synapse.Abstractions;

[RequestHandler<CreateTaskCommand, Guid>]
public class CreateTaskHandler : IRequestHandler<CreateTaskCommand, Guid>
{
    public async ValueTask<Result<Guid>> HandleAsync(CreateTaskCommand cmd, CancellationToken ct = default)
    {
        // ...
        return Result.Success(Guid.NewGuid());
    }
}
```

### `[RequestHandler<TRequest>]` — for `IRequest` (no response)

```csharp
[RequestHandler<DeleteTaskCommand>]
public class DeleteTaskHandler : IRequestHandler<DeleteTaskCommand>
{
    public async ValueTask<Result> HandleAsync(DeleteTaskCommand cmd, CancellationToken ct = default)
    {
        // ...
        return Result.Success();
    }
}
```

### `[EventHandler<TEvent>]` — for `IEventHandler<TEvent>`

```csharp
[EventHandler<TaskCreatedEvent>]
public class AuditTaskCreated : IEventHandler<TaskCreatedEvent>
{
    public ValueTask<Result> HandleAsync(TaskCreatedEvent @event, CancellationToken ct = default)
    {
        // ...
        return ValueTask.FromResult(Result.Success());
    }
}
```

### `[StreamRequestHandler<TRequest, TItem>]` — for `IStreamRequestHandler<TRequest, TItem>`

```csharp
[StreamRequestHandler<StreamTasksQuery, TaskDto>]
public class StreamTasksQueryHandler : IStreamRequestHandler<StreamTasksQuery, TaskDto>
{
    public async IAsyncEnumerable<Result<TaskDto>> HandleAsync(
        StreamTasksQuery query,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // ...
        yield return Result.Success(new TaskDto());
    }
}
```

## Generated classes

The generator produces two classes in the root namespace of your assembly.

### `RegisterGroup`

Implements `IRegisterGroup`. Contains one `RegisterXxxHandler<...>()` call per annotated handler:

```csharp
// Generated — do not edit
public sealed class RegisterGroup : IRegisterGroup
{
    public void Register(IDependencyInjectionBuilder builder)
    {
        builder.RegisterRequestHandler<CreateTaskHandler, CreateTaskCommand, Guid>();
        builder.RegisterRequestHandler<DeleteTaskHandler, DeleteTaskCommand>();
        builder.RegisterEventHandler<AuditTaskCreated, TaskCreatedEvent>();
        builder.RegisterStreamRequestHandler<StreamTasksQueryHandler, StreamTasksQuery, TaskDto>();
    }
}
```

### `EventDispatcherRegistration`

Implements `IEventDispatcherRegistration`. Contains one dispatch delegate per event type. Required for NativeAOT environments where polymorphic dispatch via `typeof(T)` at runtime is not permitted:

```csharp
// Generated — do not edit
public sealed class EventDispatcherRegistration : IEventDispatcherRegistration
{
    public void RegisterDispatchers(Action<Type, DispatchEventDelegate> register)
    {
        register(
            typeof(TaskCreatedEvent),
            static (e, dispatcher, ct) => dispatcher.DispatchAsync((TaskCreatedEvent)e, ct));
    }
}
```

## Wire up the generated classes

Pass both generated classes to `AddSynapse`:

```csharp
services.AddSynapse(cfg =>
{
    cfg.AddRegisterGroup(new RegisterGroup());
    cfg.UseEventDispatcherRegistration<EventDispatcherRegistration>();
});
```

That replaces all manual `RegisterRequestHandler` / `RegisterEventHandler` calls.

## When to use `UseEventDispatcherRegistration`

`UseEventDispatcherRegistration` is **required** when publishing as NativeAOT or with full trimming. In standard .NET runtimes it is optional but recommended for performance (avoids a dictionary lookup per event type).

## Incremental generation

The generator is an `IIncrementalGenerator` — it only re-generates when a handler class is added, removed, or its attributes change. Build times are not affected significantly in large solutions.

## See also

- [Commands and Queries](./commands-and-queries) — `IRegisterGroup` and modular registration.
- [Events](./events) — `IEventDispatcherRegistration` and polymorphic dispatch.
- [ASP.NET Core Integration](./aspnetcore) — NativeAOT setup with `CreateSlimBuilder`.
