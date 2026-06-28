# Synapse MinimalApi Example (Native AOT Ready)

This example demonstrates how to use Synapse in an ASP.NET Core application with full Native AOT support.
It is the canonical ASP.NET integration example for the library.

## Features Demonstrated

- ✅ **Commands** — with (`CreateTask → CreateTaskResult { TaskId }`) and without (`UpdateTask`, `CompleteTask`, `DeleteTask`) a typed response
- ✅ **Queries** — single item (`GetTask`) and list (`ListTasks`)
- ✅ **Streaming** — `IStreamRequest<T>` yielded via `IAsyncEnumerable` (`StreamTasks`)
- ✅ **Validation** — `RequestValidationBehavior<TRequest, TResponse>` wired up for `CreateTask`
- ✅ **Domain events** — fan-out to multiple handlers (`TaskCompletedEvent` × 2)
- ✅ **Concurrent event orchestration** — `ConcurrentEventOrchestrator`
- ✅ **Pipeline behavior ordering** — `MetricsBehavior(Order=10)` wraps `AuditBehavior(Order=20)`;
  nesting visible in stdout brackets (`▶`/`◀`). Registered via **source-gen `[PipelineBehavior]` attribute**.
- ✅ **Constraint-based open generic** — `AuditBehavior<TRequest>` targets only `IAuditableRequest`
  commands; queries receive no audit logging. The source generator's `Satisfies()` check enforces
  the constraint at **compile time** (see `RegisterGroup.g.cs` in `obj/`).
- ✅ **Short-circuiting behavior** — `AuthorizationBehavior` returns a typed
  `Result.FailUnauthorized(...)` without calling `next()` when the caller lacks the `tasks:admin`
  permission; the handler never executes. The typed `UnauthorizedFailure` maps to **401 Unauthorized**
  via `DefaultFailureHttpMapper` (not a generic 500 — see known-issue 003).
  Registered as a **runtime open-generic** with `ISecuredRequest` constraint.
- ✅ **Stream pipeline behavior** — `StreamLoggingBehavior` wraps the `IAsyncEnumerable<Result<T>>`
  chain and logs a `🔢 Streamed N items` summary after the stream completes.
  Registered as a **runtime open-generic**.
- ✅ **Correlation** — `IContext.CorrelationId` propagated to `X-Correlation-Id` response header
- ✅ **CQRS boundary enforcement** — `[assembly: EnableSynapseCqrsBoundaryEnforcement]` (generator
  wires discovered handlers; use `cfg.RegisterCqrsBoundaryEnforcement<…>()` for manually-registered ones)
- ✅ **Native AOT Compatibility** — `CreateSlimBuilder()` and JSON source generation
- ✅ **Modular monolith** — two independent assemblies communicate via a shared event contract (Orders → `OrderPlacedEvent` → Notifications), with **source-gen** on the emitter side and **manual** `cfg.RegisterEventHandler<>()` on the subscriber side

## Pipeline Behavior Registration Mechanisms

Both registration mechanisms are showcased side-by-side in `Program.cs`:

| Mechanism | API | Example | AOT |
|---|---|---|---|
| **Source-gen attribute** | `[PipelineBehavior]` (+ `IOrderedPipelineBehavior` for ordering) + `cfg.AddRegisterGroup(new RegisterGroup())` | `MetricsBehavior`, `AuditBehavior` | ✅ Compile-time closed generics |
| **Runtime open-generic** | `cfg.AddOpenGeneric*PipelineBehavior(typeof(X<>))` | `AuthorizationBehavior`, `StreamLoggingBehavior` | ⚠️ MS DI closes at resolve time; response type must be a class (not a value type) |

## Project Structure

```
MinimalApi/
├── Program.cs                          # Entry point: DI setup and endpoint mapping
├── Features/Tasks/
│   ├── Commands.cs                     # CreateTaskCommand (IAuditableRequest), PurgeCompletedTasksCommand (ISecuredRequest), …
│   ├── Queries.cs                      # GetTaskQuery, ListTasksQuery, TaskDto, …
│   ├── Events.cs                       # TaskCreatedEvent, TaskCompletedEvent, …
│   ├── PipelineContracts.cs            # IAuditableRequest, ISecuredRequest marker interfaces
│   ├── Handlers/
│   │   ├── CommandHandlers.cs          # … + PurgeCompletedTasksCommandHandler
│   │   ├── QueryHandlers.cs
│   │   └── EventHandlers.cs
│   └── Validators/
│       └── TaskValidators.cs           # CreateTaskCommandValidator
├── Infrastructure/
│   ├── TaskRepository.cs               # In-memory ConcurrentDictionary store
│   └── Pipelines/
│       ├── MetricsBehavior.cs          # [PipelineBehavior] + IOrderedPipelineBehavior (Order 10) — ordering demo
│       ├── AuditBehavior.cs            # [PipelineBehavior] + IOrderedPipelineBehavior (Order 20) — constraint-based
│       ├── AuthorizationBehavior.cs    # Runtime open-generic — short-circuit demo
│       └── StreamLoggingBehavior.cs    # Runtime open-generic — stream behavior demo
└── Http/
    ├── requests.http                   # Commands & queries
    ├── streaming.http                  # IStreamRequest<T> demo
    ├── events.http                     # Domain events & orchestration
    └── pipeline-behaviors.http         # Full behavior tour (ordering / constraint / short-circuit / stream)
```

## API Endpoints

| Method   | Path                        | Description                                                       |
|----------|-----------------------------|-------------------------------------------------------------------|
| `GET`    | `/`                         | API info                                                          |
| `GET`    | `/tasks`                    | List all tasks (Query, no audit)                                  |
| `GET`    | `/tasks/stream`             | Stream tasks one by one (IStreamRequest + StreamLoggingBehavior)  |
| `GET`    | `/tasks/{id}`               | Get a task by ID (Query, no audit)                                |
| `POST`   | `/tasks`                    | Create a task (Command → CreateTaskResult, validated + audited)   |
| `PUT`    | `/tasks/{id}`               | Update a task (Command, audited)                                  |
| `POST`   | `/tasks/{id}/complete`      | Complete a task (2 concurrent event handlers, audited)            |
| `DELETE` | `/tasks/{id}`               | Delete a task (domain event, audited)                             |
| `POST`   | `/tasks/admin/purge`        | Purge completed tasks (AuthorizationBehavior — requires `tasks:admin`) |
| `POST`   | `/orders`                   | Place an order — emits `OrderPlacedEvent` (Orders module, source-gen)  |
| `GET`    | `/notifications`            | List received notifications (Notifications module, manual registration) |

## Modular Monolith: Cross-Assembly Event Communication

Three sibling projects demonstrate decoupled inter-module communication using Synapse events:

```
MinimalApi.Modules.Contracts/   ← shared contract only (OrderPlacedEvent : IEvent)
MinimalApi.Modules.Orders/      ← emitter — [RequestHandler<>] + source-generated RegisterGroup
MinimalApi.Modules.Notifications/ ← subscriber — cfg.RegisterEventHandler<>() (no source generator)
```

**Key property:** Orders and Notifications reference only `Contracts` — they have **no reference to each other**.

### Registration contrast

| Module | API used | Where wired |
|--------|----------|-------------|
| **Orders** | `[RequestHandler<PlaceOrderCommand, Guid>]` → generator emits `RegisterGroup` | Host: `cfg.AddRegisterGroup(new Orders.RegisterGroup())` |
| **Notifications** | `cfg.RegisterEventHandler<OrderPlacedNotificationHandler, OrderPlacedEvent>()` | Host: `cfg.AddNotificationsHandlers()` |

Both are fully Native-AOT safe: the generator emits closed delegates for Orders; `RegisterEventHandler` registers the AOT-safe dispatch delegate directly for Notifications.

### Flow

```
POST /orders
  └─ PlaceOrderCommandHandler (Orders)
       └─ _emitter.EmitAsync(new OrderPlacedEvent { ... })
             └─ OrderPlacedNotificationHandler (Notifications)
                  └─ NotificationLog.Add(entry)

GET /notifications  →  returns [ { orderId, product, quantity, receivedAt }, ... ]
```

See `Http/modules.http` to try it end-to-end.

## Running the Example

### Development (JIT)

```bash
cd examples/MinimalApi
dotnet run
```

### Exploring Pipeline Behaviors

Open the themed HTTP files in your IDE's HTTP client (JetBrains, VS Code REST Client, etc.):

| File | What it shows |
|---|---|
| `Http/requests.http` | Commands, queries, validation, correlation IDs |
| `Http/streaming.http` | `IStreamRequest<T>` + `StreamLoggingBehavior` |
| `Http/events.http` | Domain events, fan-out, `ConcurrentEventOrchestrator` |
| `Http/pipeline-behaviors.http` | **Full behavior tour** — ordering, constraints, short-circuit, stream |
| `Http/modules.http` | **Modular monolith** — `POST /orders` → `OrderPlacedEvent` → `GET /notifications` |

While making requests, watch the terminal output for the behavior log lines:

```
▶ [metrics:10] CreateTaskCommand started          ← MetricsBehavior (Order=10)
📝 [audit:20] CreateTaskCommand — pipeline entry  ← AuditBehavior  (Order=20, IAuditableRequest only)
info: CreateTaskCommand handled in 00:00:00.001   ← SimpleLoggingBehavior
info: Creating task: ...                          ← handler
📝 [audit:20] CreateTaskCommand succeeded
◀ [metrics:10] CreateTaskCommand finished in ...
```

Compare with a query (no `📝 [audit]` lines):

```
▶ [metrics:10] ListTasksQuery started
info: ListTasksQuery handled in 00:00:00.001
◀ [metrics:10] ListTasksQuery finished in ...
```

And the short-circuit demo (`POST /tasks/admin/purge` without the header → **401 Unauthorized**):

```
▶ [metrics:10] PurgeCompletedTasksCommand started
🚫 [auth] PurgeCompletedTasksCommand denied — requires 'tasks:admin'
info: PurgeCompletedTasksCommand handled in ... with error ...
◀ [metrics:10] PurgeCompletedTasksCommand finished
```

### Publish as Native AOT

```bash
dotnet publish -c Release
```

The native executable will be in `bin/Release/net10.0/{runtime}/publish/`.

**Size comparison:**

| Publish mode    | Approximate size | Startup time |
|-----------------|------------------|--------------|
| Regular publish | ~90 MB           | 500–800 ms   |
| Native AOT      | ~15–25 MB        | 150–250 ms   |

## AOT-Specific Configurations

### 1. Slim Builder

```csharp
var builder = WebApplication.CreateSlimBuilder(args);
```

`CreateSlimBuilder` includes only essential services, resulting in smaller binaries.

### 2. JSON Source Generation

```csharp
[JsonSerializable(typeof(CreateTaskRequest))]
[JsonSerializable(typeof(UpdateTaskRequest))]
[JsonSerializable(typeof(TaskDto))]
[JsonSerializable(typeof(List<TaskDto>))]
[JsonSerializable(typeof(PurgeResult))]   // PurgeCompletedTasksCommand response
internal partial class AppJsonSerializerContext : JsonSerializerContext { }

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0,
        AppJsonSerializerContext.Default);
});
```

Every type used in HTTP request/response bodies must be registered so the AOT compiler
can emit the necessary serialization code.

### 3. Project Configuration

```xml
<PropertyGroup>
    <PublishAot>true</PublishAot>
    <InvariantGlobalization>true</InvariantGlobalization>
</PropertyGroup>
```

- `PublishAot` — enables Native AOT compilation on `dotnet publish`
- `InvariantGlobalization` — reduces binary size by using invariant culture only

## AOT Compatibility Notes

### ✅ Synapse is AOT-Ready

- No runtime reflection in hot paths
- Source generators for handler registration
- Trimming annotations
- `ValueTask` for minimal allocations

### ⚠️ What to Watch For

1. **Avoid Runtime Reflection**
   ```csharp
   // Bad (not AOT-friendly)
   var handler = Activator.CreateInstance(handlerType);

   // Good (AOT-friendly)
   var handler = new MyHandler(dependencies);
   ```

2. **JSON Serialization** — Always add `[JsonSerializable]` for types used in HTTP bodies.

3. **Dependency Injection** — Register services explicitly; avoid assembly scanning.

### Check for AOT Warnings

```bash
dotnet publish -c Release /p:PublishAot=true
```

Look for:
- `IL2026` — Methods that require runtime reflection
- `IL3050` — Trimming warnings

## When to Use Native AOT

### ✅ Good Fit

- Microservices and containers
- Serverless/FaaS (Azure Functions, AWS Lambda)
- CLI tools
- Resource-constrained environments
- Cold-start sensitive applications

### ❌ Not Ideal

- Applications heavy on runtime reflection
- Dynamic plugin systems
- Large dependency trees with non-AOT-ready libraries

## Next Steps

- Run `MinimalApi.Tests` for an integration test suite against these endpoints
- See [GettingStarted](../GettingStarted/README.md) for a step-by-step tutorial on Synapse fundamentals

## Learn More

- [.NET Native AOT Deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
- [Trim Warnings](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/fixing-warnings)
- [JSON Source Generation](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation)
