# 012 — Nested groups and richer group metadata

|  |  |
|---|---|
| **Status** | 🟡 Partial — flat groups work |
| **Priority** | Medium |
| **Area** | Groups |
| **Tiers** | All |
| **Breaking** | No — additive |

## Problem

Groups are one level deep and carry four things. A realistic API wants `/api` → `/api/v1` →
`/api/v1/tasks`, with policy set at one level and tags at another. Today that is one flat group per
leaf, with the shared prefix retyped in each.

## Current state

- `src/Synapse.Endpoints/Attributes/InGroupAttribute.cs` — `AllowMultiple = false`, one group per
  endpoint, and `EndpointGroup` has no notion of a parent.
- `src/Synapse.Endpoints/Builders/IEndpointGroupBuilder.cs` offers `Prefix`, `Tag`,
  `RequireAuthorization`, `Raw`. No summary, no description, no `Produces`, no filters except
  through `Raw`.
- `src/Synapse.Endpoints/Internal/GroupCache.cs` keys one `RouteGroupBuilder` per group type per
  root builder. The cache is the right shape for nesting — it just always resolves against `root`:

  ```csharp
  var created = root.MapGroup(groupBuilder.GetPrefix());
  ```

  Nesting would resolve the parent group first and map into *that* instead.

## What you cannot write today

`/api/v1` shared by every feature, `/tasks` under it:

```csharp
public sealed class TasksGroup : EndpointGroup<V1Group>
// CS0308: The non-generic type 'EndpointGroup' cannot be used with type arguments
```

Nor from the endpoint side:

```csharp
[InGroup<V1Group>]
[InGroup<TasksGroup>]     // CS0579: Duplicate 'InGroup' attribute
public sealed class GetTaskEndpoint : Endpoint<GetTaskQuery, TaskDto>;
```

So every leaf group retypes the shared prefix and the shared policy:

```csharp
public sealed class TasksGroup : EndpointGroup
{
    public override void Configure(IEndpointGroupBuilder builder) =>
        builder.Prefix("/api/v1/tasks").Tag("Tasks").RequireAuthorization("authenticated");
}

public sealed class ProjectsGroup : EndpointGroup
{
    // Move to /api/v2 and this is a find-and-replace across every group in the solution, with
    // nothing to catch the one that was missed.
    public override void Configure(IEndpointGroupBuilder builder) =>
        builder.Prefix("/api/v1/projects").Tag("Projects").RequireAuthorization("authenticated");
}
```

and anything beyond the four supported settings goes through the escape hatch, per group:

```csharp
builder.Prefix("/api/v1/tasks")
       .Raw(group => group.WithDescription("Task management")   // no Description on the builder
                          .ProducesProblem(500));               // no Produces either
```

## Proposed API

```csharp
public sealed class V1Group : EndpointGroup
{
    public override void Configure(IEndpointGroupBuilder builder) => builder.Prefix("/api/v1");
}

public sealed class TasksGroup : EndpointGroup<V1Group>          // parent as a type argument
{
    public override void Configure(IEndpointGroupBuilder builder) =>
        builder.Prefix("/tasks").Tag("Tasks").RequireAuthorization();
}
```

Plus the missing metadata methods on `IEndpointGroupBuilder`:

```csharp
IEndpointGroupBuilder Summary(string summary);
IEndpointGroupBuilder Description(string description);
IEndpointGroupBuilder Produces(int statusCode);
IEndpointGroupBuilder ProducesProblem(int statusCode);
```

### With the proposal

Declared once, inherited down:

```csharp
public sealed class ApiGroup : EndpointGroup
{
    public override void Configure(IEndpointGroupBuilder builder) => builder.Prefix("/api");
}

public sealed class V1Group : EndpointGroup<ApiGroup>
{
    public override void Configure(IEndpointGroupBuilder builder) =>
        builder.Prefix("/v1")
               .RequireAuthorization("authenticated")
               .ProducesProblem(StatusCodes.Status500InternalServerError);
}

public sealed class TasksGroup : EndpointGroup<V1Group>
{
    public override void Configure(IEndpointGroupBuilder builder) =>
        builder.Prefix("/tasks").Tag("Tasks").Description("Task management");
}
```

```csharp
[Get("/{taskId:guid}")]
[InGroup<TasksGroup>]
public sealed class GetTaskEndpoint : Endpoint<GetTaskQuery, TaskDto>;
// → GET /api/v1/tasks/{taskId:guid}, authenticated, tagged Tasks, declaring 500
```

`ApiGroup` and `V1Group` are each configured once no matter how many descendants map through them
(the `GroupCache` invariant, extended rather than bypassed), and an endpoint still opts out locally:

```csharp
[Get("/health")]
[InGroup<V1Group>]
public sealed class HealthEndpoint : Endpoint<HealthQuery, HealthDto>
{
    public override void Configure(IEndpointBuilder<HealthDto> builder) =>
        builder.AllowAnonymous();      // beats RequireAuthorization from any ancestor level
}
```

## Acceptance criteria

- [ ] Parent groups are resolved and configured once each, regardless of how many descendants map
      through them (extend the `GroupCache` invariant, do not bypass it).
- [ ] A cycle in the parent chain is a generator diagnostic, not a stack overflow at startup.
- [ ] `AllowAnonymous` on an endpoint still overrides an inherited `RequireAuthorization` from any
      ancestor level.
- [ ] Prefixes compose in declaration order and the duplicate-route check sees the composed result.
- [ ] `SYNE006` (`InGroup` type does not derive from `EndpointGroup`) extended to the parent type
      argument.
