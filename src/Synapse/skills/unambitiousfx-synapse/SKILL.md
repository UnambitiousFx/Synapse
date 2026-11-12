---
name: unambitiousfx-synapse
description: Write C# code with the UnambitiousFx.Synapse mediator — commands, queries, events, handlers, pipeline behaviors, validators, outbox and registration (source generator or AddSynapse). Use when a .NET project references UnambitiousFx.Synapse, or when adding/changing a request, handler, event, behavior or validator.
---

# UnambitiousFx.Synapse

In-process mediator. Every handler returns `Result`/`Result<T>` from `UnambitiousFx.Functional` (see the `unambitiousfx-functional` skill for Result itself).

Namespaces: `UnambitiousFx.Synapse.Abstractions` (messages, handler interfaces, attributes, `IInvoker`, `IEmitter`), `UnambitiousFx.Synapse` (`AddSynapse`, `ValidateSynapse`), `UnambitiousFx.Synapse.Pipelines` (built-in behaviors), `UnambitiousFx.Functional`.

## Rules

- Expected failures are **returned** (`Result.Failure...`), never thrown. Exceptions are for misconfiguration only.
- Handlers return `ValueTask<Result>` / `ValueTask<Result<T>>` and accept the `CancellationToken`; pass it on.
- Handlers are **classes**, not records (the generator ignores records — `SYN103`).
- Every handler must be registered: Synapse does **not** scan by convention. Prefer the source generator attributes.
- Cross-cutting concerns (logging, validation, transactions, auth) go in pipeline behaviors, not in handlers.
- Don't dispatch a command from inside a query handler (or vice versa); CQRS enforcement throws.

## Messages

```csharp
public record CreateTaskCommand(string Title) : ICommand<Guid>;   // = IRequest<Guid>, marks intent
public record DeleteTaskCommand(Guid TaskId) : ICommand;          // no value, only success/failure
public record GetTaskQuery(Guid TaskId) : IQuery<TaskDto>;
public record StreamTasksQuery : IStreamRequest<TaskDto>;
public record TaskCreated(Guid TaskId, string Title) : IEvent;
```

`ICommand`/`IQuery` are optional markers over `IRequest`/`IRequest<T>`; they are useful to scope behaviors.

## Handlers (with source generator attributes)

```csharp
[RequestHandler<CreateTaskCommand, Guid>]
public sealed class CreateTaskHandler(ITaskRepository repo, IEmitter emitter)
    : ICommandHandler<CreateTaskCommand, Guid>                      // alias of IRequestHandler<,>
{
    public async ValueTask<Result<Guid>> HandleAsync(CreateTaskCommand cmd, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cmd.Title))
        {
            return Result.FailValidation<Guid>("Title is required.");
        }

        var id = await repo.AddAsync(cmd.Title, ct);
        await emitter.EmitAsync(new TaskCreated(id, cmd.Title), ct);
        return Result.Success(id);
    }
}

[RequestHandler<DeleteTaskCommand>]
public sealed class DeleteTaskHandler : ICommandHandler<DeleteTaskCommand>
{
    public ValueTask<Result> HandleAsync(DeleteTaskCommand cmd, CancellationToken ct = default) =>
        ValueTask.FromResult(Result.Success());
}

[EventHandler<TaskCreated>]
public sealed class AuditTaskCreated : IEventHandler<TaskCreated>
{
    public ValueTask<Result> HandleAsync(TaskCreated @event, CancellationToken ct = default) =>
        ValueTask.FromResult(Result.Success());
}

[StreamRequestHandler<StreamTasksQuery, TaskDto>]
public sealed class StreamTasksHandler : IStreamRequestHandler<StreamTasksQuery, TaskDto>
{
    public async IAsyncEnumerable<Result<TaskDto>> HandleAsync(
        StreamTasksQuery query, [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return Result.Success(new TaskDto());
    }
}
```

No async work? Derive from `RequestHandler<TRequest, TResponse>` and override `protected Result<TResponse> Handle(TRequest request)`.

## Registration

With `UnambitiousFx.Synapse.Generator` installed, the attributes generate a `RegisterGroup` class in the project's **root namespace** (or fill your own `[RegisterGroup] public partial class X;`). Register every assembly's group:

```csharp
builder.Services.AddSynapse(cfg =>
{
    cfg.AddRegisterGroup(new RegisterGroup());           // one per assembly that has handlers
    cfg.ValidateOnStart();                               // fail startup on misconfiguration
});
```

Manual alternative (no generator):

```csharp
cfg.RegisterRequestHandler<CreateTaskHandler, CreateTaskCommand, Guid>();
cfg.RegisterRequestHandler<DeleteTaskHandler, DeleteTaskCommand>();
cfg.RegisterEventHandler<AuditTaskCreated, TaskCreated>();
cfg.RegisterStreamRequestHandler<StreamTasksHandler, StreamTasksQuery, TaskDto>();
```

Never mix both for the same handler (duplicate request handlers → `SYN002`, last one wins).

## Sending and publishing

```csharp
Result<Guid> created = await invoker.InvokeAsync(new CreateTaskCommand("Buy milk"), ct); // TResponse inferred
Result deleted = await invoker.InvokeAsync(new DeleteTaskCommand(id), ct);
await foreach (Result<TaskDto> item in invoker.InvokeStreamAsync(new StreamTasksQuery(), ct)) { }

Result emitted = await emitter.EmitAsync(new TaskCreated(id, title), ct);               // EmitMode.Default
await emitter.EmitAsync(new TaskCreated(id, title), EmitMode.Outbox, ct);              // deferred
await emitter.EmitBatchAsync(events, ct);
```

`EmitAsync` returns a `Result`: check or propagate it. All handlers of an event run (sequentially by default; `cfg.SetEventOrchestrator<ConcurrentEventOrchestrator>()` for parallel).

## Pipeline behaviors

Open-generic + `[PipelineBehavior]` = applied (AOT-safe) to every matching handler in this assembly and the ones it references. Generic constraints scope it:

```csharp
[PipelineBehavior]
public sealed class TransactionBehavior<TRequest, TResponse>
    : IRequestPipelineBehavior<TRequest, TResponse>, IOrderedPipelineBehavior
    where TRequest : ICommand<TResponse>                 // commands only
    where TResponse : notnull
{
    public uint Order => IOrderedPipelineBehavior.Middle; // lower runs first (outermost)

    public async ValueTask<Result<TResponse>> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TRequest, TResponse> next,
        CancellationToken cancellationToken = default)
    {
        var result = await next(request, cancellationToken); // or short-circuit: return Result.Failure<TResponse>(...)
        return result;
    }
}
```

- Void requests: `IRequestPipelineBehavior<TRequest>` with `RequestHandlerDelegate<TRequest>`; events: `IEventPipelineBehavior<TEvent>` with `EventHandlerDelegate<TEvent>`; streams: `IStreamRequestPipelineBehavior<TRequest, TItem>` whose `next()` takes no argument.
- Order comes from `IOrderedPipelineBehavior.Order` (`First`/`Middle`/`Last`), **not** registration order.
- Prefer relative ordering over magic numbers across modules/packages: `[PipelineBehavior(Stage = PipelineStage.Authorization, After = typeof(ActorBehavior<,>))]` (stages: `Observability`, `Authentication`, `Authorization`, `Validation`, `Transaction`, `Handler`). Without the generator: `cfg.ConfigureBehaviorOrdering(typeof(AuditBehavior<,>), PipelineStage.Authorization, after: typeof(ActorBehavior<,>))`. Resolution: stage, then Before/After, then `Order`, then registration. A behavior without a stage falls in the stage whose `Order` band holds its `Order`, so Order-only pipelines are unchanged. Cycles: MDG018 at build, SYN005 at `ValidateSynapse`.
- Behavior from another package: `[assembly: SynapseGlobalBehavior(typeof(Shared.LoggingBehavior<,>))]` in the host, and register the host's own `RegisterGroup`.
- CQRS enforcement for the whole graph (host only):
  `[assembly: SynapseGlobalBehavior(typeof(CqrsBoundaryEnforcementBehavior<>))]` and `[assembly: SynapseGlobalBehavior(typeof(CqrsBoundaryEnforcementBehavior<,>))]`.
- Closed manual registration: `cfg.RegisterRequestPipelineBehavior<TBehavior, TRequest, TResponse>()`.
- Logic that does not depend on the response type (validation, auth, exception mapping): write it **once** as `IResultPipelineBehavior<TRequest, TResult> where TResult : IResult` instead of two classes. `next.InvokeAsync(request, ct)` continues, `next.Fail(failure)` short-circuits with the right `Result`/`Result<T>`. Same `[PipelineBehavior]` / `IOrderedPipelineBehavior` rules.

## Validation

```csharp
[Validator]   // generator registers the validator and RequestValidationBehavior
public sealed class CreateTaskValidator : IRequestValidator<CreateTaskCommand>
{
    public ValueTask<Result> ValidateAsync(CreateTaskCommand cmd, CancellationToken ct = default) =>
        ValueTask.FromResult(string.IsNullOrWhiteSpace(cmd.Title)
            ? Result.FailValidation("Title is required.")
            : Result.Success());
}
```

Without the generator: `cfg.AddValidator<CreateTaskValidator, CreateTaskCommand, Guid>()` **and** `cfg.RegisterRequestPipelineBehavior<RequestValidationBehavior<CreateTaskCommand, Guid>, CreateTaskCommand, Guid>()`. A failing validator stops the pipeline; the handler is not called.

FluentValidation: reference `UnambitiousFx.Synapse.FluentValidation` and call `services.AddSynapseFluentValidation()`; every registered `IValidator<TRequest>` then runs for `Result` and `Result<T>` requests and a failure returns a `ValidationFailure` with field, message and code per error. Do not hand-write a FluentValidation behavior.

## Authorization

Reference `UnambitiousFx.Synapse.Authorization`, register your `ICurrentPrincipal` (scoped), then `services.AddSynapseAuthorization(o => { o.QueryScope = "read"; o.CommandScope = "write"; o.Policy<MyResourceAccessPolicy>(); })`. Every request then runs, in order: actor (anonymous → `UnauthenticatedFailure`, unless the request is `IAllowAnonymous`, e.g. sign-in; that also skips the default scope but not its own role/scope/resource markers), role (`IRequiresRole`), scope (`IRequiresScope`, else query → `QueryScope`, command → `CommandScope`; `o.DisableDefaultScopes()` turns the default off), resource (`IResourceScoped { ResourceId; Access }` checked by the `IResourceAccessPolicy`; no policy → denied). Denials return `UnauthorizedFailure` and are passed to every `IAuthorizationDenialRecorder` (`o.DenialRecorder<T>()`). Do not hand-write these behaviors; put your own behavior between two checks with the `AuthorizationBehaviorOrder` constants.

## Transactions

Mark a request `ITransactional` (`record CloseEnvelope(Guid Id) : ICommand, ITransactional;`) and call `cfg.AddTransactions<TManager>()`: the handler runs inside `ITransactionManager.BeginAsync`, committed on a successful `Result`, rolled back on a failure or exception. Do not open transactions by hand in handlers. EF Core: `services.AddEfCoreTransactions(t => t.Enlist<ModuleDbContext>().Enlist<OutboxDbContext>())` then `cfg.AddTransactions<EfCoreTransactionManager>()`; every enlisted context must share one Scoped `DbConnection`, nested transactional commands get savepoints, and the outbox is flushed after the outermost commit. Native AOT: `[assembly: SynapseGlobalBehavior(typeof(TransactionBehavior<,>))]` plus `cfg.AddTransactionManager<TManager>()` instead.

## Outbox

`EmitMode.Outbox` stores the event; it is dispatched by `IOutboxCommit.CommitAsync(ct)` (call it after your transaction commits) or by `cfg.AddOutboxDispatcher(...)` in the background. Each entry is dispatched in a **new DI scope**: handlers do not share the caller's `DbContext`. Durable storage: `UnambitiousFx.Synapse.Outbox.EntityFrameworkCore` or `.AdoNet`. If the app never uses the outbox, `cfg.DisableInMemoryOutboxProductionWarning()` silences the startup warning about the in-memory storage in Production.

## Testing

- Unit-test a handler by calling `HandleAsync` directly with substitutes for its dependencies; assert with `UnambitiousFx.Functional.xunit` (`result.ShouldBe().Success()`).
- Configuration test: `var report = provider.ValidateSynapse(); Assert.True(report.IsValid, report.ToString());`
- `IPipelineDescriber.Describe<TRequest, TResponse>()` lists handlers and behaviors in execution order.

## Pitfalls

- A request with no registered handler throws `MissingHandlerException` at dispatch.
- A `[PipelineBehavior]` declared in a library does not wrap handlers of the app that references it (propagation is downward only). Put global behaviors in the composition root.
- `[assembly: SynapseGlobalBehavior]` does nothing unless that assembly's `RegisterGroup` is passed to `AddRegisterGroup` (`SYN105`).
- Your own `ICommand`/`IQuery<T>` types collide with the built-in markers (CS0104): alias or migrate.

Full docs: https://unambitiousfx.com/lib-synapse/
