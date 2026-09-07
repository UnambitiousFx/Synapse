# 005 — Self-handled endpoint tier

|  |  |
|---|---|
| **Status** | ✅ Shipped |
| **Priority** | High |
| **Area** | Base classes |
| **Tiers** | `SelfHandledEndpoint<TRequest, TResponse>`, `SelfHandledEndpoint<TRequest>` |
| **Breaking** | No — additive base class |

## Problem

Every tier that binds a request also forces the request through the mediator. There is no way to
write "bind this, run this code, return this" — the classic REPR shape where the endpoint *is* the
handler.

That is a real cost for endpoints with no domain logic to speak of (a lookup, a health projection, a
lightweight read model): each one needs a message type, a handler type, a DI registration and a
generator round-trip, so that the mediator can hand back a value the endpoint could have computed
itself.

## Current state

- `src/Synapse.Endpoints/RawEndpoint.Generic.cs` constrains `TRequest : IRequest<TResponse>` and
  resolves `IHttpInvoker` unconditionally inside a `sealed` `HandleAsync`.
- `src/Synapse.Endpoints/Endpoint.Generic.cs` adds the generated binder on top of that constraint.
- The only mediator-free tier is `RawEndpoint` (`src/Synapse.Endpoints/RawEndpoint.cs`), which drops
  binding, success mapping and automatic OpenAPI metadata along with it. The jump from "generated
  binder plus declarative responses" to "here is an `HttpContext`" is the whole cliff.

## What you cannot write today

A liveness projection with no domain message behind it:

```csharp
public sealed record ProbeQuery
{
    public required string Probe { get; init; }
}

[Get("/health/{probe}")]
public sealed class ProbeEndpoint : Endpoint<ProbeQuery, ProbeDto>
// CS0311: The type 'ProbeQuery' cannot be used as type parameter 'TRequest' in the generic type
//         'Endpoint<TRequest, TResponse>'. There is no implicit reference conversion from
//         'ProbeQuery' to 'IRequest<ProbeDto>'.
{
    // …and there is nowhere to put the three lines of logic anyway: HandleAsync is sealed and
    // dispatches through IHttpInvoker.
}
```

So a route that reads one dictionary costs four moving parts:

```csharp
public sealed record ProbeQuery : IRequest<ProbeDto>          // 1. the message, now mediator-shaped
{
    public required string Probe { get; init; }
}

public sealed class ProbeQueryHandler : IRequestHandler<ProbeQuery, ProbeDto>   // 2. the handler
{
    public ValueTask<Result<ProbeDto>> HandleAsync(ProbeQuery query, CancellationToken ct) =>
        ValueTask.FromResult(Result.Success(new ProbeDto { Probe = query.Probe, Healthy = true }));
}

services.AddSynapse(cfg =>
    cfg.RegisterRequestHandler<ProbeQueryHandler, ProbeQuery, ProbeDto>());     // 3. registration

[Get("/health/{probe}")]
public sealed class ProbeEndpoint : Endpoint<ProbeQuery, ProbeDto>;             // 4. the endpoint
```

The alternative is dropping to `RawEndpoint`, which trades the mediator for everything else:

```csharp
[Get("/health/{probe}")]
public sealed class ProbeEndpoint : RawEndpoint
{
    public override ValueTask<IResult> HandleAsync(HttpContext context, CancellationToken ct)
    {
        // No generated binder, so this is hand-written; no declared success status unless
        // Configure says so; no failure mapping — every Result has to be turned into an IResult
        // here, by hand, consistently with every other endpoint in the app.
        if (!context.TryGetRoute("probe", out var probe))
        {
            return ValueTask.FromResult(context.Validate().Problem());
        }

        return ValueTask.FromResult<IResult>(TypedResults.Ok(new ProbeDto { Probe = probe!, Healthy = true }));
    }
}
```

## Proposed API

A tier that keeps the generated binder and the response builder, and replaces dispatch with an
overridable handler:

```csharp
[Get("/tasks/{taskId:guid}")]
public sealed class GetTaskEndpoint : SelfHandledEndpoint<GetTaskRequest, TaskDto>
{
    public override async ValueTask<Result<TaskDto>> ExecuteAsync(GetTaskRequest request, HttpContext context, CancellationToken ct)
    {
        var repository = context.Service<ITaskRepository>();
        return await repository.FindAsync(request.TaskId, ct);
    }
}
```

`TRequest` carries no `IRequest<T>` constraint. Returning `Result<TResponse>` keeps failures flowing
through the same `IFailureHttpMapper` the mediator tiers use, so the two tiers answer identically.

### With the proposal

Four parts collapse to one, and nothing above the handler changes:

```csharp
public sealed record ProbeQuery                    // no IRequest<T>, no handler, no registration
{
    public required string Probe { get; init; }
}

[Get("/health/{probe}")]
public sealed class ProbeEndpoint : SelfHandledEndpoint<ProbeQuery, ProbeDto>
{
    public override void Configure(IEndpointBuilder<ProbeDto> builder)
    {
        builder.Ok().ProducesProblem(StatusCodes.Status404NotFound);
    }

    public override async ValueTask<Result<ProbeDto>> ExecuteAsync(ProbeQuery request,
        HttpContext context, CancellationToken ct)
    {
        var probes = context.Service<IProbeRegistry>();

        return await probes.FindAsync(request.Probe, ct) is { } probe
            ? Result.Success(new ProbeDto { Probe = probe.Name, Healthy = probe.IsHealthy })
            : Result.FailNotFound<ProbeDto>("Probe", request.Probe);
    }
}
```

`Probe` still binds from the route through the generated binder, a bad request is still the same
accumulated `400`, and `Result.FailNotFound` still goes through the registered `IFailureHttpMapper`
— so this endpoint's `404` is byte-for-byte the `404` `GetTaskEndpoint` sends through the mediator.

## Acceptance criteria

- [x] Generated binder works for a `TRequest` that is not an `IRequest<T>` (verify the generator's
      discovery keys off the endpoint base type, not the message interface).
- [x] Failures map through `IFailureHttpMapper` exactly as dispatched failures do.
- [x] Declarative success mappers (`Ok`/`Created`/`Accepted`/`NoContent`/`StatusCode`) and
      `OnSuccess` behave as on `Endpoint<TRequest, TResponse>`.
- [x] A void arity (`SelfHandledEndpoint<TRequest>`) exists to match.
- [x] Documented alongside the tier table in `docs/docs/endpoints/reference/base-classes.mdx`, with
      explicit guidance on when *not* to use it (anything a pipeline behaviour must wrap).

## Notes

The trade-off is real and should be stated in the docs rather than smoothed over: a self-handled
endpoint gets no pipeline behaviours, so no validation stage, no retries, no CQRS boundary
enforcement, no outbox. It is for endpoints that genuinely have no domain message.

## Resolution

Two base classes in `src/Synapse.Endpoints`, siblings of `MappedEndpoint<…>` under
`BoundEndpoint<TBound>` — not under `RawEndpoint<TRequest, TResponse>`, whose `IRequest<TResponse>`
constraint is the thing being dropped. Each supplies the same two seams every bound tier supplies:
`BindBoundAsync` calls the `BindAsync` generated into the endpoint's own partial, and
`ProduceResultAsync` runs
`ExecuteAsync` and maps its `Result`. The lifecycle order, the hooks and the processors are inherited
unchanged, so the new tier cannot drift from the others.

The handler is named **`ExecuteAsync`**, not `HandleAsync` as proposed above:
`HandleAsync(HttpContext, CancellationToken)` is already declared and sealed on `BoundEndpoint<TBound>`,
so a three-argument `HandleAsync` would have compiled as an overload while offering an author two
same-named members of which only one can be overridden.

Failures are mapped by resolving `IFailureHttpMapper` from the request services and calling
`AsHttpBuilder` — the same mapper instance and the same call `HttpInvoker` makes internally, rather
than a new member on `IHttpInvoker`, which would have been a breaking change for implementers of a
public interface. `SelfHandledEndpointTests.Invoke_WhenExecuteAsyncFails_AnswersIdenticallyToTheSameFailureDispatched`
pins the parity by sending the same `NotFoundFailure` through both tiers and comparing status,
content type and body.

Generator side: two metadata names, two `EndpointKind` values, and the arms that follow from them.
The `RawEndpointFree` arm had to move last in the base-chain switch — every tier derives from
`RawEndpoint`, so a walk that reached it first classified the new tier as the free-form low level
(mapped, but with no binder emitted).
