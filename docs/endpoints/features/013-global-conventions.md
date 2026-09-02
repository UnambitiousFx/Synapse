# 013 — Global endpoint conventions

|  |  |
|---|---|
| **Status** | 🔴 Missing |
| **Priority** | Medium |
| **Area** | Registration |
| **Tiers** | All |
| **Breaking** | No — additive overload |

## Problem

There is no place to say "every Synapse endpoint in this app gets X". An app-wide prefix, a default
authorization policy, a default tag, a filter, a common `ProducesProblem(500)` — each has to be
repeated in every endpoint's `Configure`, or in every group.

## Current state

`src/Synapse.Endpoints/EndpointRouteBuilderExtensions.cs`:

```csharp
public static IEndpointRouteBuilder MapSynapseEndpoints(this IEndpointRouteBuilder endpoints,
    params IEndpointGroup[] groups)
```

No configuration parameter. The method maps each generated group, then runs the duplicate-route
check. `MapEndpoint<TEndpoint>` returns a `RouteHandlerBuilder`, so a caller *can* configure one
endpoint at a time — but the generated `IEndpointGroup.Map` implementations discard that return
value, which is exactly the path most apps use.

## What you cannot write today

```csharp
app.MapSynapseEndpoints(options =>
{
    options.Prefix = "/api";
    options.ConfigureEach = route => route.ProducesProblem(500).RequireRateLimiting("default");
}, TasksEndpoints.Group);
// CS1503: Argument 1: cannot convert from 'lambda expression' to
//         'UnambitiousFx.Synapse.Endpoints.IEndpointGroup'
```

There is no overload — the only parameter is `params IEndpointGroup[]`. And the return value that
would let a caller post-process is discarded by the generated `IEndpointGroup.Map` implementations,
so this does not work either:

```csharp
// MapEndpoint<T> does return a RouteHandlerBuilder, but nothing hands you the ones that
// MapSynapseEndpoints created.
var route = app.MapEndpoint<GetTaskEndpoint>().ProducesProblem(500);   // one endpoint, by hand
```

So the app-wide policy is copied into every endpoint:

```csharp
public sealed class GetTaskEndpoint : Endpoint<GetTaskQuery, TaskDto>
{
    public override void Configure(IEndpointBuilder<TaskDto> builder) =>
        builder.ProducesProblem(StatusCodes.Status500InternalServerError)   // in all of them
               .Raw(route => route.RequireRateLimiting("default"))          // in all of them
               .ProducesProblem(StatusCodes.Status404NotFound);             // this one's own
}
```

or into every group, which is the same duplication one level up — and neither can express "and
nothing else in this application", which is what a convention is for.

## Proposed API

```csharp
app.MapSynapseEndpoints(options =>
{
    options.Prefix = "/api";
    options.ConfigureEach = route => route.ProducesProblem(500).RequireRateLimiting("default");
    options.DefaultAuthorizationPolicy = "authenticated";
}, AppEndpoints.Group, OrdersEndpoints.Group);
```

Ordering must be documented and tested: conventions apply *before* an endpoint's own `Configure`, so
a per-endpoint call always wins over a global default — otherwise `AllowAnonymous` on one endpoint
cannot escape a global policy.

### With the proposal

```csharp
app.MapSynapseEndpoints(options =>
{
    options.Prefix = "/api";
    options.ConfigureEach = route => route.ProducesProblem(500).RequireRateLimiting("default");
    options.DefaultAuthorizationPolicy = "authenticated";
}, TasksEndpoints.Group, OrdersEndpoints.Group);
```

Every endpoint above keeps only what is its own:

```csharp
public sealed class GetTaskEndpoint : Endpoint<GetTaskQuery, TaskDto>
{
    public override void Configure(IEndpointBuilder<TaskDto> builder) =>
        builder.ProducesProblem(StatusCodes.Status404NotFound);
}
```

Ordering is the part that has to be pinned by a test, not just documented — conventions apply
*before* `Configure`, so the endpoint always wins:

```csharp
[Get("/health")]
public sealed class HealthEndpoint : Endpoint<HealthQuery, HealthDto>
{
    public override void Configure(IEndpointBuilder<HealthDto> builder) =>
        builder.AllowAnonymous();   // escapes options.DefaultAuthorizationPolicy
}
```

and a hand-written route is untouched, because `ConfigureEach` is scoped by `SynapseEndpointMarker`:

```csharp
app.MapGet("/version", () => Version);   // no rate limit, no 500 declaration, no policy
```

## Acceptance criteria

- [ ] `ConfigureEach` applies to every Synapse endpoint and to no hand-written `app.MapGet`.
      (`SynapseEndpointMarker` already identifies them.)
- [ ] A global prefix composes correctly with group prefixes, and the duplicate-route check sees the
      composed routes.
- [ ] Per-endpoint configuration overrides global configuration, with a test for `AllowAnonymous`
      against a global policy.
- [ ] The existing parameter-less overload keeps working unchanged.

## Notes

Watch the warning already recorded in `ThrowOnDuplicateRoutes`: reading `dataSource.Endpoints`
materialises endpoints early and re-runs accumulated conventions. Everything added today is
idempotent metadata. A convention supplied by a user through `ConfigureEach` may not be — so either
document the constraint loudly or apply conventions where they cannot be run twice.
