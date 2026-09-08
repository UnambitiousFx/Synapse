# 017 — Dependency injection into endpoints

|  |  |
|---|---|
| **Status** | 🟡 Partial — works via service location |
| **Priority** | Low |
| **Area** | Lifetime / DI |
| **Tiers** | All |
| **Breaking** | Potentially — a per-request lifetime changes instance semantics |

## Problem

Endpoints are startup-created singletons with no constructor injection, so every dependency is
fetched inside the handler:

```csharp
var repository = context.Service<ITaskRepository>();
var clock      = context.Service<IClock>();
```

This works and is fast, but it is service location: dependencies are invisible in the type's
signature, cannot be substituted in a constructor for a test, and the compiler does not know they
are required.

## Current state

- `src/Synapse.Endpoints/EndpointRouteBuilderExtensions.cs`:

  ```csharp
  var endpoint = new TEndpoint();
  var descriptor = endpoint.CreateDescriptor(metadata);
  ```

  One instance per mapped endpoint, created with `new`, constrained `where TEndpoint : SynapseEndpoint, new()`.
- The constraint is deliberate and documented on every tier: *"Endpoints are stateless singletons:
  one instance is created at startup, `Configure` runs once, and the same instance serves every
  request. Constructor injection is therefore unavailable by design."*
- `SYNE010` reports an endpoint shape that cannot satisfy the `new()` constraint.
- `HttpContextBindingExtensions.Service<T>` is the sanctioned access path.

## What you cannot write today

```csharp
[Get("/tasks/{taskId:guid}")]
public sealed class GetTaskEndpoint(ITaskRepository repository, IClock clock)
    : Endpoint<GetTaskQuery, TaskDto>;
```

Two reports, both accurate:

```
error SYNE010: 'GetTaskEndpoint' has no public parameterless constructor, so
  'MapEndpoint<TEndpoint>()' (which requires 'TEndpoint : SynapseEndpoint, new()') cannot be
  instantiated for it. Make the endpoint a top-level, non-generic class with a public parameterless
  constructor.

error CS0310: 'GetTaskEndpoint' must be a non-abstract type with a public parameterless constructor
  in order to use it as parameter 'TEndpoint' in the generic method
  'EndpointRouteBuilderExtensions.MapEndpoint<TEndpoint>(IEndpointRouteBuilder)'
```

The second one surfaces inside the *generated* group file, since that is where `MapEndpoint` is
called — a compile error in code the user did not write and cannot edit.

So dependencies stay implicit:

```csharp
public override IResult OnSuccess(TaskDto response, HttpContext context)
{
    // Nothing in this type's signature says either of these is required. Miss a registration and
    // it is a runtime InvalidOperationException on the first request that reaches this line,
    // not a startup failure and not a compile error.
    var repository = context.Service<ITaskRepository>();
    var clock      = context.Service<IClock>();
    …
}
```

## Proposed API

Two options, in increasing cost:

**A. `[FromServices]` properties**, populated per request by the generated binder. Keeps the
singleton, keeps the AOT story, makes dependencies visible on the type:

```csharp
[FromServices] public ITaskRepository Repository { get; init; }
```

Note this only works if the *message* is per-request — which it is — not the endpoint. So the
property belongs on the request type, which is arguably the wrong home for a repository. Weigh this
carefully; it may be worse than the status quo.

**B. Per-request endpoint instances**, resolved from `HttpContext.RequestServices`, with constructor
injection and `Configure` still run once against a startup-created prototype. Costs an allocation
and a resolve per request, and splits the "one instance" invariant the docs lean on heavily.

### With the proposal

**A. `[FromServices]` on the message** — visible, per-request, no lifetime change:

```csharp
public sealed record GetTaskQuery : IRequest<TaskDto>
{
    public required Guid TaskId { get; init; }

    // …and this is the objection, not a detail: the repository is now part of the message
    // contract, so every non-HTTP dispatch of GetTaskQuery has to supply it, and it will be
    // serialized unless [JsonIgnore] says otherwise.
    [FromServices] public required ITaskRepository Repository { get; init; }
}
```

**B. Per-request endpoint instances** — the familiar spelling, at a measurable cost:

```csharp
[Get("/tasks/{taskId:guid}")]
public sealed class GetTaskEndpoint(ITaskRepository repository, IClock clock)
    : Endpoint<GetTaskQuery, TaskDto>
{
    // Configure still runs once, against a startup-created prototype. HandleAsync runs against
    // an instance resolved from HttpContext.RequestServices — one allocation and one resolve per
    // request, and the "one instance serves every request" invariant the docs lean on is gone.
    public override void Configure(IEndpointBuilder<TaskDto> builder) =>
        builder.ProducesProblem(StatusCodes.Status404NotFound);
}
```

opted into per endpoint, so the singleton path stays the default:

```csharp
services.AddSynapse(cfg => cfg.AddScopedEndpoint<GetTaskEndpoint>());
```

Neither is obviously better than `context.Service<T>()`, which is why this file is ranked last: A
moves a repository into a message contract, and B trades the package's central claim for
constructor syntax. The testability argument for either one is answered more cheaply by
[003](003-endpoint-test-harness.md).

## Acceptance criteria

- [ ] If B is chosen: benchmark it in `benchmarks/SynapseBenchmark` against the singleton path and
      publish the delta — this package's premise is low allocation, so the number decides the design.
- [ ] The singleton path stays the default; anything else is opt-in per endpoint.
- [ ] `Configure` still runs exactly once regardless of instance lifetime.
- [ ] `SYNE010`'s message updated if the `new()` constraint is relaxed.

## Notes

Listed for completeness and ranked last on purpose. The current design is coherent and its rationale
is written down in several places; changing it needs a stronger reason than familiarity. The
[test harness](003-endpoint-test-harness.md) removes most of the practical pain, since the usual
argument for constructor injection is testability.
