# 016 — Link generation for Location headers

|  |  |
|---|---|
| **Status** | 🟡 Partial — `Created` works, but only with hand-built URLs |
| **Priority** | Low |
| **Area** | Responses |
| **Tiers** | `Endpoint<…>`, `RawEndpoint<…>`, `MappedEndpoint<…>` |
| **Breaking** | No — additive overload |

## Problem

`Created` takes a delegate that returns a URL string, so the `Location` header is built by string
interpolation:

```csharp
builder.Created(id => $"/tasks/{id}");
```

The route template is now written twice — once in the `[Get]` attribute of the read endpoint, once
inside a string in the write endpoint — with nothing keeping them in step. Rename the route and the
`Location` header silently points at a `404`.

## Current state

- `src/Synapse.Endpoints/Builders/IEndpointBuilder.Generic.cs`:

  ```csharp
  IEndpointBuilder<TResponse> Created(Func<TResponse, string> location);
  IEndpointBuilder<TResponse> Accepted(Func<TResponse, string>? location = null);
  ```

- `Name(string name)` already exists on every builder and sets the endpoint name used for link
  generation — so the naming half of the feature is present and unused by `Created`.
- ASP.NET Core's `LinkGenerator` is resolvable from `HttpContext.RequestServices`, but the success
  mapper signature is `Func<TResponse, IResult>` with no `HttpContext`, so it cannot reach it.

## What you cannot write today

```csharp
builder.CreatedAtRoute("GetTask", created => new { taskId = created.TaskId });
// CS1061: 'IEndpointBuilder<TaskCreated>' does not contain a definition for 'CreatedAtRoute'
```

The declarative mapper cannot reach a `LinkGenerator`, because its delegate is handed the response
and nothing else:

```csharp
builder.Created(created =>
{
    // Func<TResponse, string>. No HttpContext, so no RequestServices, so no LinkGenerator —
    // and no scheme or host either, which is why the Location is relative.
    return $"/tasks/{created.TaskId}";
});
```

`Name("GetTask")` already exists and already sets the endpoint name link generation needs, so the
route *is* addressable — just not from here. The one place with an `HttpContext` is `OnSuccess`,
and going there costs the declaration:

```csharp
public sealed class CreateTaskEndpoint : Endpoint<CreateTaskCommand, TaskCreated>
{
    public override void Configure(IEndpointBuilder<TaskCreated> builder)
    {
        // Declaring 201 here makes the OnSuccess below dead code:
        //   SYNE004: 'CreateTaskEndpoint' overrides OnSuccess and also calls a declarative success
        //   method (Ok/Created/Accepted/NoContent/StatusCode) on the builder in Configure. The
        //   declarative mapping always wins — OnSuccess is never called.
        // Remove it and the endpoint documents a 200 while sending a 201.
        builder.StatusCode(StatusCodes.Status201Created);
    }

    public override IResult OnSuccess(TaskCreated response, HttpContext context)
    {
        var links = context.Service<LinkGenerator>();
        var location = links.GetUriByName(context, "GetTask", new { taskId = response.TaskId });

        return TypedResults.Created(location, response);
    }
}
```

So the working shape today is the interpolated string, with the template written twice — once in
`GetTaskEndpoint`'s `[Get("/{taskId:guid}")]` under a group prefix of `/tasks`, once inside
`$"/tasks/{created.TaskId}"` in `CreateTaskEndpoint`. Change the group prefix and the `Location`
header points at a `404`, with nothing to catch it: this is `examples/EndpointsApi`'s
`CreateTaskEndpoint` exactly as it stands.

## Proposed API

```csharp
builder.CreatedAtRoute("GetTask", r => new { taskId = r.Id });
builder.AcceptedAtRoute("GetTaskStatus", r => new { taskId = r.Id });
```

This needs the success mapper to receive the `HttpContext`, i.e. an internal
`Func<TResponse, HttpContext, IResult>` alongside the existing shape. `OnSuccess` already takes the
context, so the two converge rather than diverge.

### With the proposal

```csharp
[Get("/{taskId:guid}")]
[InGroup<TasksGroup>]
public sealed class GetTaskEndpoint : Endpoint<GetTaskQuery, TaskDto>
{
    public override void Configure(IEndpointBuilder<TaskDto> builder) =>
        builder.Name("GetTask").ProducesProblem(StatusCodes.Status404NotFound);
}

[Post("/")]
[InGroup<TasksGroup>]
public sealed class CreateTaskEndpoint : Endpoint<CreateTaskCommand, TaskCreated>
{
    public override void Configure(IEndpointBuilder<TaskCreated> builder) =>
        builder.CreatedAtRoute("GetTask", created => new RouteValueDictionary
        {
            ["taskId"] = created.TaskId,
        });
}
```

The template is written once, the group prefix is applied by the same code that mapped the route,
and the `Location` header is absolute:

```
HTTP/1.1 201 Created
Location: https://localhost:5001/tasks/9f0c…

# and if "GetTask" is not a mapped route name, that is a startup failure naming both the
# endpoint and the missing name — never an empty Location.
```

A `RouteValueDictionary` rather than an anonymous type, deliberately: the anonymous-type spelling is
the familiar one and needs reflection over its properties, which is what the package exists to
avoid.

## Acceptance criteria

- [ ] `CreatedAtRoute` resolves the URL through `LinkGenerator` using the request's scheme and host.
- [ ] An unknown route name fails at startup where possible, and otherwise throws a message naming
      the endpoint and the missing route name — never emits an empty `Location`.
- [ ] The existing `Created(Func<TResponse, string>)` overload keeps working unchanged.
- [ ] AOT-safe: the route-values object must not require reflection over an anonymous type, or the
      overload takes a `RouteValueDictionary` instead.

## Notes

The AOT constraint probably decides the API. An anonymous-type route-values argument is the familiar
MVC spelling but is exactly the kind of reflection this package exists to avoid; prefer an explicit
`RouteValueDictionary` or a generated overload.
