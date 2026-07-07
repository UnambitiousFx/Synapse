# Synapse MinimalApi Example (Native AOT Ready)

The canonical ASP.NET Core integration example for Synapse. It is built to teach two things clearly:

1. **Synapse with and without the source generator** — the same handlers and behaviors can be wired
   either by attributes the generator discovers, or by explicit runtime calls.
2. **Each assembly registers its own handlers/behaviors and plugs into the host** — a module is a
   self-contained library; the host composes it with a single call.

Everything runs under **Native AOT**.

## The big idea: attributes declare, the host composes

Open `Program.cs`. Almost every cross-cutting concern is declared by an **attribute**, so the
`AddSynapse(...)` body is just composition:

```csharp
builder.Services.AddSynapse(cfg =>
{
    cfg.AddRegisterGroup(new MinimalApi.RegisterGroup());          // host (Tasks) — source-gen
    cfg.AddRegisterGroup(new Modules.Orders.RegisterGroup());      // Orders module — source-gen
    cfg.AddNotificationsHandlers();                                // Notifications module — manual
    cfg.SetEventOrchestrator<ConcurrentEventOrchestrator>();
});
```

The behaviors themselves are declared above `Program`, at the **composition root**:

```csharp
[assembly: SynapseGlobalBehavior(typeof(CqrsBoundaryEnforcementBehavior<>))]
[assembly: SynapseGlobalBehavior(typeof(CqrsBoundaryEnforcementBehavior<,>))]
[assembly: SynapseGlobalBehavior(typeof(SimpleLoggingBehavior<>))]
[assembly: SynapseGlobalBehavior(typeof(SimpleLoggingEventBehavior<>))]
```

These four lines register open-generic behaviors **once**. The generator closes each over every
matching handler — **including handlers in the referenced Orders and Notifications assemblies** — and
emits one closed, AOT-safe registration per match. That is the whole "the host applies a behavior to
plugged-in modules" story in four attributes.

## Registration mechanisms (all shown side-by-side)

| Mechanism | API | Used by | AOT |
|---|---|---|---|
| **Source-gen handler/validator** | `[RequestHandler<>]` / `[EventHandler<>]` / `[StreamRequestHandler<>]` / `[Validator]` → generated `RegisterGroup` | Tasks (host), Orders module | ✅ closed at compile time |
| **Source-gen behavior (owned)** | `[PipelineBehavior]` (+ `IOrderedPipelineBehavior`) on a type **you** declare | `Metrics`, `Audit`, `Authorization`, `StreamLogging`, Orders' `OrderTracing` | ✅ closed at compile time |
| **Global behavior (composition root)** | `[assembly: SynapseGlobalBehavior(typeof(X<>))]` for behaviors you **don't** own (e.g. shipped in a package); applies cross-assembly | `CqrsBoundaryEnforcement`, `SimpleLogging`, `SimpleLoggingEvent` | ✅ closed at compile time |
| **Manual (no generator)** | `cfg.RegisterEventHandler<THandler, TEvent>()` | Notifications module | ✅ explicit closed registration |

> Use `[PipelineBehavior]` for an open generic you own; use `[assembly: SynapseGlobalBehavior]` for
> one you can't decorate at the source (a referenced package, or the built-in Synapse behaviors).
> Both feed the same generator path and emit closed registrations. Add
> `[assembly: DisableSynapseCrossAssemblyBehaviors]` to scope global behaviors to the current
> assembly's handlers only.

## Pipeline order

Position is decided by each behavior's `IOrderedPipelineBehavior.Order` (default `Last`), **not** by
registration order:

```
[CqrsBoundaryEnforcement]            ← global; First (outermost)
  [Authorization:5]                  ← [PipelineBehavior]; only ISecuredRequest (short-circuit)
    [Metrics:10]                     ← [PipelineBehavior]; unconstrained
      [Audit:20]                     ← [PipelineBehavior]; only IAuditableRequest
        [SimpleLogging]              ← global; ref-type requests/events
          [RequestValidation]        ← from [Validator]; Last
            [Handler]
```

## Features demonstrated

- ✅ **Commands** — with (`CreateTask → CreateTaskResult`) and without (`UpdateTask`, `CompleteTask`,
  `DeleteTask`) a typed response
- ✅ **Queries** — single (`GetTask`) and list (`ListTasks`)
- ✅ **Streaming** — `IStreamRequest<T>` via `IAsyncEnumerable` (`StreamTasks`), wrapped by
  `StreamLoggingBehavior` (source-gen `[PipelineBehavior]`)
- ✅ **Validation** — `[Validator]` on `CreateTaskCommandValidator` auto-wires
  `RequestValidationBehavior`
- ✅ **Domain events** — fan-out to multiple handlers (`TaskCompletedEvent` × 2) via
  `ConcurrentEventOrchestrator`
- ✅ **Behavior ordering** — `Metrics(10)` wraps `Audit(20)`; visible in stdout brackets `▶`/`◀`
- ✅ **Constraint-based open generic** — `AuditBehavior` only on `IAuditableRequest`; queries get none
- ✅ **Short-circuit** — `AuthorizationBehavior` returns `Result.FailUnauthorized(...)` without calling
  `next()`; maps to **401 Unauthorized**
- ✅ **Value-type response under AOT** — `PurgeCompletedTasks → int`, `PlaceOrder → Guid` through
  closed CQRS + behavior registrations
- ✅ **Correlation** — `IContext.CorrelationId` → `X-Correlation-Id` response header
- ✅ **CQRS boundary enforcement** — via `[assembly: SynapseGlobalBehavior(...)]`, outermost
- ✅ **Modular monolith** — two independent assemblies communicate via a shared event contract
  (Orders → `OrderPlacedEvent` → Notifications); the host's global behaviors cover both

## Modular monolith: cross-assembly events

Three sibling projects show decoupled inter-module communication:

```
MinimalApi.Modules.Contracts/      ← shared contract only (OrderPlacedEvent : IEvent)
MinimalApi.Modules.Orders/         ← emitter — [RequestHandler<>] + source-generated RegisterGroup
MinimalApi.Modules.Notifications/  ← subscriber — cfg.RegisterEventHandler<>() (no source generator)
```

**Key property:** Orders and Notifications reference only `Contracts` — never each other.

| Module | Registration | Wired in host by |
|--------|--------------|------------------|
| **Orders** | `[RequestHandler<PlaceOrderCommand, Guid>]` → generated `RegisterGroup` | `cfg.AddRegisterGroup(new Orders.RegisterGroup())` |
| **Notifications** | `cfg.RegisterEventHandler<OrderPlacedNotificationHandler, OrderPlacedEvent>()` | `cfg.AddNotificationsHandlers()` |

Because the host declares `[assembly: SynapseGlobalBehavior(typeof(SimpleLoggingBehavior<>))]` etc.,
the host's logging/metrics behaviors **also** run for the modules' handlers — confirmed in stdout:

```
▶ [metrics:10] PlaceOrderCommand started      ← host MetricsBehavior on the Orders handler
▶ [orders:trace:15] PlaceOrderCommand started ← Orders module's own behavior
…
Dispatching event OrderPlacedEvent to 1 handler(s)
Event OrderPlacedEvent handled in 00:00:00…   ← host SimpleLoggingEventBehavior on the cross-assembly event
```

Flow:

```
POST /orders
  └─ PlaceOrderCommandHandler (Orders)  →  _emitter.EmitAsync(OrderPlacedEvent)
        └─ OrderPlacedNotificationHandler (Notifications)  →  NotificationLog.Add(entry)

GET /notifications  →  [ { orderId, product, quantity, receivedAt }, … ]
```

## API endpoints

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/` | API info |
| `GET` | `/tasks` | List tasks (Query, no audit) |
| `GET` | `/tasks/stream` | Stream tasks (`IStreamRequest` + `StreamLoggingBehavior`) |
| `GET` | `/tasks/{id}` | Get a task (Query, no audit) |
| `POST` | `/tasks` | Create a task (Command → `CreateTaskResult`, validated + audited) |
| `PUT` | `/tasks/{id}` | Update a task (Command, audited) |
| `POST` | `/tasks/{id}/complete` | Complete a task (2 concurrent event handlers, audited) |
| `DELETE` | `/tasks/{id}` | Delete a task (domain event, audited) |
| `POST` | `/tasks/admin/purge` | Purge completed tasks — requires `X-User-Permissions: tasks:admin` (short-circuit → 401) |
| `POST` | `/orders` | Place an order — emits `OrderPlacedEvent` (Orders module, source-gen) |
| `GET` | `/notifications` | List received notifications (Notifications module, manual) |

## Running

```bash
cd examples/MinimalApi
dotnet run
```

Open the themed HTTP files in your IDE's HTTP client and run requests top-to-bottom:

| File | What it shows |
|---|---|
| `Http/requests.http` | Commands, queries, validation, correlation IDs |
| `Http/streaming.http` | `IStreamRequest<T>` + `StreamLoggingBehavior` |
| `Http/events.http` | Domain events, fan-out, `ConcurrentEventOrchestrator` |
| `Http/pipeline-behaviors.http` | Behavior tour — ordering, constraints, short-circuit, stream |
| `Http/modules.http` | Modular monolith — `POST /orders` → `OrderPlacedEvent` → `GET /notifications` |

## Native AOT

The project sets `<PublishAot>true</PublishAot>` and `<InvariantGlobalization>true</InvariantGlobalization>`,
uses `WebApplication.CreateSlimBuilder(args)`, and registers every HTTP body type in a
`JsonSerializerContext`:

```csharp
[JsonSerializable(typeof(CreateTaskRequest))]
[JsonSerializable(typeof(TaskDto))]
[JsonSerializable(typeof(List<TaskDto>))]
[JsonSerializable(typeof(int))]    // PurgeCompletedTasks → int (value-type response under AOT)
// …
internal partial class AppJsonSerializerContext : JsonSerializerContext { }
```

Synapse is AOT-ready: source-generated registration, no hot-path reflection, trimming annotations,
`ValueTask`. Publish and check for `IL2026`/`IL3050` warnings:

```bash
dotnet publish -c Release
```

| Publish mode | Approx. size | Startup |
|---|---|---|
| Regular | ~90 MB | 500–800 ms |
| Native AOT | ~15–25 MB | 150–250 ms |

## Next steps

- `MinimalApi.Tests` — integration tests against these endpoints
- [GettingStarted](../GettingStarted/README.md) — step-by-step tutorial on Synapse fundamentals
- [.NET Native AOT Deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
