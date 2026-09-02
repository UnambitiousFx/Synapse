# 011 — Multiple routes and verbs per endpoint

|  |  |
|---|---|
| **Status** | 🔴 Missing |
| **Priority** | Medium |
| **Area** | Routing / Builders |
| **Tiers** | All |
| **Breaking** | No — additive |

## Problem

One endpoint class serves exactly one verb at exactly one route. Three ordinary shapes are therefore
impossible without duplicating the class:

- An alias or a deprecated path kept alive alongside the new one.
- `GET` and `HEAD` on the same resource.
- `PUT` and `PATCH` handled by one upsert endpoint.

## Current state

- `src/Synapse.Endpoints/Attributes/HttpEndpointAttribute.cs` and the verb attributes are all
  `AllowMultiple = false`, so a second attribute *of the same class* will not compile. Two
  different ones do compile, and `EndpointsGenerator.ReadRouteAttribute` reads only the first —
  silently, with no diagnostic.
- `src/Synapse.Endpoints/Builders/EndpointBuilderCore.cs` — `Route` *overwrites* rather than
  accumulates:

  ```csharp
  _httpMethods = [method.ToUpperInvariant()];
  _route = template;
  ```

  So calling `builder.Get("/a").Get("/b")` in `Configure` silently keeps only `/b`.
- `RawEndpointPlan` and `EndpointDescriptor` do carry `HttpMethods` as a `string[]`, so the plumbing
  below the builder is already multi-verb; only the surface is single.

## What you cannot write today

Keeping a renamed route alive, and answering `HEAD` alongside `GET`:

```csharp
[Get("/tasks/{taskId:guid}")]
[Get("/v1/tasks/{taskId:guid}")]   // CS0579: Duplicate 'Get' attribute
public sealed class GetTaskEndpoint : Endpoint<GetTaskQuery, TaskDto>;
```

A second verb *does* compile — `[Get]` and `[HttpEndpoint]` are different attribute classes, so the
`AllowMultiple = false` check does not catch it — and is then dropped without a word:

```csharp
[Get("/tasks/{taskId:guid}")]
[HttpEndpoint("HEAD", "/tasks/{taskId:guid}")]   // compiles; never mapped
public sealed class GetTaskEndpoint : Endpoint<GetTaskQuery, TaskDto>;
```

`EndpointsGenerator.ReadRouteAttribute` returns the **first** attribute whose class chain reaches
`HttpEndpointAttribute` and ignores every other one, with no diagnostic. Attribute order decides
which of the two survives.

The builder compiles and is worse, because it fails silently:

```csharp
public override void Configure(IEndpointBuilder<TaskDto> builder)
{
    // Route() overwrites: _httpMethods = [method]; _route = template. Only the last call
    // survives, so /tasks/{taskId:guid} is never mapped and nothing says so.
    builder.Get("/tasks/{taskId:guid}")
           .Get("/v1/tasks/{taskId:guid}");
}
```

So an alias means a second class, duplicated in full:

```csharp
[Get("/tasks/{taskId:guid}")]
public sealed class GetTaskEndpoint : Endpoint<GetTaskQuery, TaskDto>
{
    public override void Configure(IEndpointBuilder<TaskDto> builder) =>
        builder.ProducesProblem(StatusCodes.Status404NotFound).Name("GetTask");
}

[Get("/v1/tasks/{taskId:guid}")]
public sealed class GetTaskV1Endpoint : Endpoint<GetTaskQuery, TaskDto>
{
    // The same Configure, retyped — and the two must be kept in step by hand. If the routes ever
    // resolve their properties differently (one names the parameter {id}, the other {taskId}),
    // SYNE013 warns that only one binder is emitted for GetTaskQuery and the other endpoint binds
    // through it.
    public override void Configure(IEndpointBuilder<TaskDto> builder) =>
        builder.ProducesProblem(StatusCodes.Status404NotFound).Name("GetTaskV1");
}
```

## Proposed API

```csharp
[Get("/tasks/{id:guid}")]
[Get("/v1/tasks/{id:guid}")]        // AllowMultiple = true
public sealed class GetTaskEndpoint : Endpoint<GetTaskQuery, TaskDto> { }
```

and on the builder, accumulating rather than replacing:

```csharp
builder.Routes("/tasks/{id:guid}", "/v1/tasks/{id:guid}")
       .Verbs("GET", "HEAD");
```

### With the proposal

```csharp
[Get("/tasks/{taskId:guid}")]
[Get("/v1/tasks/{taskId:guid}")]                 // AllowMultiple = true
[HttpEndpoint("HEAD", "/tasks/{taskId:guid}")]   // read, not discarded
public sealed class GetTaskEndpoint : Endpoint<GetTaskQuery, TaskDto>
{
    public override void Configure(IEndpointBuilder<TaskDto> builder)
    {
        builder.ProducesProblem(StatusCodes.Status404NotFound).Name("GetTask");
    }
}
```

or, when the set is computed:

```csharp
builder.Routes("/tasks/{taskId:guid}", "/v1/tasks/{taskId:guid}")
       .Verbs("GET", "HEAD");
```

One class, one `Configure`, one binder, three route-table entries — and the startup duplicate check
still fires for two *different* endpoints on the same template.

The upsert case, which is the one that needs the per-verb answer:

```csharp
[Put("/tasks/{taskId:guid}")]
[Patch("/tasks/{taskId:guid}")]
public sealed class UpsertTaskEndpoint : Endpoint<UpsertTaskCommand>;
```

Both verbs carry a body, so `Accepts` is declared for both. Mix a bodyless verb in —
`[Get]` + `[Post]` on one class — and `DeclaresRequestBody` must answer per verb, or the `GET`
operation declares a request body it will never read (`docs/known-issues/067`).

## Acceptance criteria

- [ ] Multiple routes map to multiple entries in the route table sharing one endpoint instance.
- [ ] The duplicate-route check in `MapSynapseEndpoints` still catches genuine collisions across
      endpoints without reporting an endpoint's own aliases.
- [ ] `SYNE009` (route declared both by attribute and in `Configure`) still fires correctly.
- [ ] `DeclaresRequestBody` is evaluated per verb — a `GET`+`POST` endpoint must not declare a body
      for the `GET` (`docs/known-issues/067`).
- [ ] OpenAPI emits one operation per route/verb pair.

## Notes

The last acceptance criterion is the sharp one. `DeclaresRequestBody(string[] httpMethods)` currently
takes the whole set and answers once via `HttpMethodHelpers.AllVerbsAreBodyless`. A mixed set needs a
per-verb answer, or the `Accepts` declaration will be wrong for half the routes.
