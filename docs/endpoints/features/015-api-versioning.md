# 015 — API versioning

|  |  |
|---|---|
| **Status** | 🔴 Missing — and actively blocked |
| **Priority** | Medium |
| **Area** | Routing |
| **Tiers** | All |
| **Breaking** | Possibly — the duplicate-route check must relax |

## Problem

Versioned endpoints are not merely unsupported; the documented workaround throws at startup.

The design points users at `Raw` to enable `Asp.Versioning`, which disambiguates two endpoints
sharing a route template via a matcher policy. But the startup duplicate-route check cannot see
matcher policies, so two *Synapse* endpoints disambiguated that way are reported as a collision and
the app fails to boot.

## Current state

`src/Synapse.Endpoints/EndpointRouteBuilderExtensions.cs`, in the remarks on
`ThrowOnDuplicateRoutes` — the behaviour is deliberate and documented:

> a template legitimately duplicated but disambiguated by a matcher policy (API versioning, which the
> design points at `Raw` to enable) threw at startup instead of working. Two Synapse endpoints
> disambiguated that way are still reported: this check cannot see a matcher policy, so it stays
> conservative about the endpoints it is actually responsible for.

The check was already narrowed once — from the whole route table down to endpoints carrying
`SynapseEndpointMarker` — which fixed the hand-written-`MapGet` half of the problem and left this
half open.

## What you cannot write today

Two versions of one resource, disambiguated by the documented `Raw` + `Asp.Versioning` route:

```csharp
[Get("/tasks/{taskId:guid}")]
public sealed class GetTaskV1Endpoint : Endpoint<GetTaskQuery, TaskDtoV1>
{
    public override void Configure(IEndpointBuilder<TaskDtoV1> builder) =>
        builder.Raw(route => route.HasApiVersion(1.0));
}

[Get("/tasks/{taskId:guid}")]
public sealed class GetTaskV2Endpoint : Endpoint<GetTaskQuery2, TaskDtoV2>
{
    public override void Configure(IEndpointBuilder<TaskDtoV2> builder) =>
        builder.Raw(route => route.HasApiVersion(2.0));
}
```

The application does not start:

```
Unhandled exception. System.InvalidOperationException: More than one Synapse endpoint claims the
same HTTP method and route: GET /tasks/{taskId:guid}. Give each endpoint a distinct route, or
check whether a group prefix collides with an endpoint's own route template.
   at UnambitiousFx.Synapse.Endpoints.EndpointRouteBuilderExtensions.ThrowOnDuplicateRoutes(…)
   at UnambitiousFx.Synapse.Endpoints.EndpointRouteBuilderExtensions.MapSynapseEndpoints(…)
```

The matcher policy that would have separated them at request time is invisible to a startup check
over route templates, so the check is right about what it can see and wrong about the application.
There is no opt-out, which leaves two options, both of which give up versioning as a concept:

```csharp
// A. Put the version in the template. Then it is not a version — header and media-type
//    versioning are unavailable, and clients cannot negotiate.
[Get("/v1/tasks/{taskId:guid}")]
[Get("/v2/tasks/{taskId:guid}")]   // …and see 011: this does not compile either.

// B. One endpoint, one message, branching on the version inside the handler.
//    Both response shapes now live in one type and the OpenAPI document describes neither.
```

## Proposed API

Minimum viable fix — an opt-out, so versioning stops being a hard block:

```csharp
builder.AllowDuplicateRoute();   // this endpoint is disambiguated by a matcher policy
```

First-class support, if versioning is deemed in scope:

```csharp
[Get("/tasks/{id:guid}")]
[ApiVersion("1.0")]
public sealed class GetTaskV1Endpoint : Endpoint<GetTaskQuery, TaskDtoV1> { }
```

with the version participating in the duplicate check's key, and group-level
`builder.Version("1.0")` for a whole group (composes with [012](012-nested-groups.md)).

### With the proposal

The minimum viable fix — the two endpoints above boot, with the check told why:

```csharp
public override void Configure(IEndpointBuilder<TaskDtoV1> builder)
{
    builder.AllowDuplicateRoute()      // disambiguated by a matcher policy, not by template
           .Raw(route => route.HasApiVersion(1.0));
}
```

First-class, if versioning is in scope — the version participates in the duplicate check's key, so
a genuine collision still throws:

```csharp
[Get("/tasks/{taskId:guid}")]
[ApiVersion("1.0")]
public sealed class GetTaskV1Endpoint : Endpoint<GetTaskQuery, TaskDtoV1>;

[Get("/tasks/{taskId:guid}")]
[ApiVersion("2.0")]
public sealed class GetTaskV2Endpoint : Endpoint<GetTaskQuery2, TaskDtoV2>;

[Get("/tasks/{taskId:guid}")]
[ApiVersion("2.0")]                    // still a startup failure: same template, same version
public sealed class GetTaskV2AliasEndpoint : Endpoint<GetTaskQuery2, TaskDtoV2>;
```

or once per group, composing with [012](012-nested-groups.md):

```csharp
public sealed class V2Group : EndpointGroup<ApiGroup>
{
    public override void Configure(IEndpointGroupBuilder builder) => builder.Version("2.0");
}
```

## Acceptance criteria

- [ ] Two endpoints on the same template disambiguated by version boot successfully.
- [ ] A genuine collision — same template, same version — still throws with the existing message.
- [ ] The OpenAPI document groups operations per version.
- [ ] `Asp.Versioning` integration is either supported or explicitly documented as out of scope.

## Notes

The opt-out is small and unblocks users today. Do that first even if first-class versioning is
deferred, and reference this file from the `ThrowOnDuplicateRoutes` remarks so the constraint and its
escape hatch are documented in the same place.
