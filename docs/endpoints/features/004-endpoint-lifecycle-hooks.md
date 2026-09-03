# 004 — Endpoint lifecycle hooks (pre/post processors)

|  |  |
|---|---|
| **Status** | ✅ Shipped |
| **Priority** | High |
| **Area** | Endpoint pipeline |
| **Tiers** | `Endpoint<…>`, `RawEndpoint<…>`, `MappedEndpoint<…>`, `StreamEndpoint<…>` |
| **Breaking** | No — additive virtual members |

## Problem

There is no seam between binding, dispatch and response mapping. An endpoint that needs to resolve a
tenant from a header before dispatch, stamp a correlation header on the way out, or audit the bound
message has nowhere to put that code except inside `BindAsync` (wrong tier — that is the low level's
job) or in a global ASP.NET filter reached through `Raw` (wrong scope — not per-endpoint, and it
sees an opaque `object`, not the bound message).

REPR implementations treat these hooks as a core feature; Synapse endpoints have none.

## Current state

`HandleAsync` is `sealed` on every message-bound tier and hard-codes the sequence:

`src/Synapse.Endpoints/RawEndpoint.Generic.cs`

```csharp
var bound = await BindAsync(context);
if (!bound.IsSuccess) { return bound.Problem(); }
var configuration = Mapped(_configuration);
var invoker = context.RequestServices.GetRequiredService<IHttpInvoker>();
return await invoker.InvokeAsync(bound.Value!, response => …, cancellationToken);
```

The only override points are `BindAsync` (before), `OnSuccess` (after, success only) and the
declarative success mappers. Nothing runs on the failure path, and nothing sees the bound message
before dispatch.

## What you cannot write today

Resolve a tenant before dispatch, stamp a header on the way out — the standard reason to wrap an
exchange:

```csharp
[Post("/tasks")]
public sealed class CreateTaskEndpoint : Endpoint<CreateTaskCommand, TaskCreated>
{
    // CS0239: 'CreateTaskEndpoint.HandleAsync(HttpContext, CancellationToken)' cannot override
    //         inherited member 'RawEndpoint<CreateTaskCommand, TaskCreated>.HandleAsync(…)'
    //         because it is sealed.
    public override async ValueTask<IResult> HandleAsync(HttpContext context, CancellationToken ct)
    {
        var tenant = context.Service<ITenantResolver>().Resolve(context);
        if (tenant is null) { return TypedResults.Problem(statusCode: 409); }

        var result = await base.HandleAsync(context, ct);
        context.Response.Headers["X-Tenant"] = tenant.Id;
        return result;
    }
}
```

Neither open seam reaches the requirement:

```csharp
// Sealed on Endpoint<…> — the generated binder owns it. Overridable one tier down on
// RawEndpoint<…>, but that means hand-writing the binding to get a hook, and it still runs
// *before* there is a bound message to inspect.
public override ValueTask<BindResult<CreateTaskCommand>> BindAsync(HttpContext context);

// Success only. Never runs for a mapped failure, never runs for a bind failure, and cannot
// short-circuit dispatch because dispatch has already happened.
public override IResult OnSuccess(TaskCreated response, HttpContext context);
```

Which leaves an ASP.NET filter through `Raw`:

```csharp
builder.Raw(route => route.AddEndpointFilter(async (invocation, next) =>
{
    // invocation.Arguments is the mapped route handler's parameter list, and EndpointMapper maps
    // `async (HttpContext context) => await descriptor.InvokeAsync(context)`. So the only argument
    // is the HttpContext. The bound CreateTaskCommand does not exist yet — binding happens inside
    // HandleAsync, below this filter — so there is nothing typed to audit or reject on.
    var result = await next(invocation);
    invocation.HttpContext.Response.Headers["X-Tenant"] = "…";
    return result;
}));
```

Per-endpoint, but blind: it sees an `HttpContext` and an opaque result, at both ends.

## Proposed API

Virtual hooks on the message-bound tiers, all no-ops by default so nothing changes for existing
endpoints:

```csharp
/// Runs after binding succeeds, before dispatch. Return a result to short-circuit.
protected virtual ValueTask<IResult?> OnBeforeHandleAsync(TRequest request, HttpContext context, CancellationToken ct);

/// Runs after dispatch, whatever the outcome, before the result is written.
protected virtual ValueTask<IResult> OnAfterHandleAsync(IResult result, HttpContext context, CancellationToken ct);

/// Runs when binding fails, before the 400 is written. Return a result to replace it.
protected virtual ValueTask<IResult> OnBindFailedAsync(BindResult<TRequest> bound, HttpContext context, CancellationToken ct);
```

A shared processor form, so a hook can be written once and reused across endpoints:

```csharp
builder.PreProcessor<ResolveTenant>()
       .PostProcessor<StampCorrelationHeader>();
```

### With the proposal

The endpoint above, expressible:

```csharp
[Post("/tasks")]
public sealed class CreateTaskEndpoint : Endpoint<CreateTaskCommand, TaskCreated>
{
    protected override ValueTask<IResult?> OnBeforeHandleAsync(CreateTaskCommand request,
        HttpContext context, CancellationToken ct)
    {
        // The bound message is here, typed, before anything is dispatched.
        var tenant = context.Service<ITenantResolver>().Resolve(context);

        return ValueTask.FromResult<IResult?>(tenant is null
            ? TypedResults.Problem(statusCode: StatusCodes.Status409Conflict)   // short-circuits
            : null);                                                            // carry on
    }

    protected override ValueTask<IResult> OnAfterHandleAsync(IResult result, HttpContext context,
        CancellationToken ct)
    {
        // Runs for the 201 and for the 404 the failure mapper wrote, which is the point.
        context.Response.Headers["X-Tenant"] = context.Service<ITenantResolver>().Resolve(context)?.Id;
        return ValueTask.FromResult(result);
    }
}
```

Written once and shared, for the cross-cutting half:

```csharp
public override void Configure(IEndpointBuilder<TaskCreated> builder)
{
    builder.PreProcessor<ResolveTenant>()          // resolved from HttpContext.RequestServices
           .PostProcessor<StampCorrelationHeader>()
           .Created(created => $"/tasks/{created.TaskId}");
}
```

## Acceptance criteria

- [ ] Hooks run in a documented order and are skipped when they are not overridden (no allocation,
      no virtual dispatch cost on the default path — verify with a benchmark).
- [ ] A short-circuiting `OnBeforeHandleAsync` prevents dispatch entirely.
- [ ] `OnAfterHandleAsync` sees mapped failure results, not only successes.
- [ ] Shared processors resolve from `HttpContext.RequestServices`.
- [ ] Benchmark added in `benchmarks/SynapseBenchmark` confirming no regression when unused.

## Notes

Decide deliberately whether these overlap with Synapse pipeline behaviours. They do not: a behaviour
wraps *message dispatch* and has no `HttpContext`; these wrap *the HTTP exchange* and cannot be
expressed as behaviours. Say so in the docs, or users will ask.

## As shipped

Two things differ from the proposal above, both deliberate:

- **Pre-processors run before binding**, not after it. They are `HttpContext`-shaped, so they have no
  use for the bound message, and running first lets a rejection avoid deserializing a body it is
  about to discard. The typed `OnBeforeHandleAsync` still runs after binding, as proposed.
- **`OnAfterHandleAsync` and post-processors run on the bind-failure path too**, not only "after
  dispatch". A correlation-header processor that silently skipped every `400` would be a bug.

Processors are shaped on the `HttpContext` rather than on `TRequest` because `IEndpointBuilder<TResponse>`
is typed on the response: a typed processor would have needed a new builder interface threaded
through every bound tier's `Configure`, which is a breaking change, to buy something the hooks
already provide. `StreamEndpoint<…>` was added to the tier list for consistency — leaving it out
would have made it the one tier where a registered processor silently did nothing.
