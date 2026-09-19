# 014 — Typed result unions / multi-outcome responses

|  |  |
|---|---|
| **Status** | 🔴 Missing |
| **Priority** | Medium |
| **Area** | Responses / OpenAPI |
| **Tiers** | `Endpoint<…>`, `BoundEndpoint<…>`, `ContractEndpoint<…>` |
| **Breaking** | No — additive |

## Problem

An endpoint declares exactly one success status. An endpoint that legitimately answers `200` or
`201` depending on whether an upsert created something, or `200` or `304` on a conditional `GET`,
cannot say so. Its OpenAPI document names one of the two and is wrong about the other.

## Current state

- `src/Synapse.Endpoints/Builders/EndpointConfiguration.cs` holds a single
  `Func<TResponse, IResult>? SuccessMapper`, a single `int? DeclaredSuccessStatusCode` and a single
  `bool SuccessResponseHasBody`.
- `RawEndpoint.Generic.cs` emits one `ProducesResponseMetadata` from those three values.
- `OnSuccess(TResponse, HttpContext)` *can* return different results per response — nothing stops it
  — but the metadata is computed at startup from the configuration, so the document still claims one
  status. The gap is in what can be *declared*, not in what can be *returned*.
- `SYNE003` and `SYNE004` already police the success-mapping surface and would need to understand
  any new shape.

## What you cannot write today

An upsert: `201` when it created the task, `200` when it replaced one.

```csharp
public override void Configure(IEndpointBuilder<UpsertResult> builder)
{
    // Both compile. EndpointConfiguration holds one SuccessMapper, one DeclaredSuccessStatusCode
    // and one SuccessResponseHasBody, so the second call overwrites the first and only 201 is
    // declared — for both outcomes.
    builder.Ok()
           .Created(result => $"/tasks/{result.Task.Id}");
}
```

Returning the right thing per request *is* possible, and is exactly where the document goes wrong:

```csharp
public override IResult OnSuccess(UpsertResult response, HttpContext context)
{
    return response.WasCreated
        ? TypedResults.Created($"/tasks/{response.Task.Id}", response.Task)
        : TypedResults.Ok(response.Task);
}
```

Correct at runtime, and the published contract disagrees with it:

- With no declarative call, the declared success status falls back to `200`, so the `201` this
  endpoint really sends is undocumented and a generated client has no `Created` model.
- Add `StatusCode(201)` to fix the metadata and `SYNE004` fires — *"the declarative mapping always
  wins — OnSuccess is never called"* — because it does, and the runtime branch above becomes dead
  code.

The conditional `GET` has the same shape and no answer either:

```csharp
public override IResult OnSuccess(TaskDto response, HttpContext context)
{
    // 304 has no body, so it cannot even be expressed as "the success type, with a status" —
    // and Produces(304) declares a response this endpoint's configuration says it cannot send.
    return context.Request.Headers.IfNoneMatch == response.ETag
        ? TypedResults.StatusCode(StatusCodes.Status304NotModified)
        : TypedResults.Ok(response);
}
```

## Proposed API

Declare the alternatives, keep the mapping in one place:

```csharp
builder.Outcomes(o => o
    .Ok()
    .Created(r => $"/tasks/{r.Id}", when: r => r.WasCreated)
    .Produces(StatusCodes.Status304NotModified));
```

Or accept `Results<TResult1, TResult2, …>` as `TResponse` and read the declared statuses off
`IEndpointMetadataProvider`, which is how minimal APIs already solve this — cheaper to implement and
familiar, at the cost of pushing a framework type into the message contract.

### With the proposal

The upsert, declared where the metadata is computed:

```csharp
[Put("/tasks/{taskId:guid}")]
public sealed class UpsertTaskEndpoint : Endpoint<UpsertTaskCommand, UpsertResult>
{
    public override void Configure(IEndpointBuilder<UpsertResult> builder)
    {
        builder.Outcomes(o => o
                   .Created(r => $"/tasks/{r.Task.Id}", when: r => r.WasCreated)
                   .Ok())                                    // the fallback, matched last
               .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
```

```json title="/openapi/v1.json — one operation, both outcomes"
{
  "200": { "content": { "application/json": { "schema": { "$ref": "#/components/schemas/UpsertResult" } } } },
  "201": { "content": { "application/json": { "schema": { "$ref": "#/components/schemas/UpsertResult" } } } },
  "400": { "…": "validation problem" },
  "404": { "…": "problem" }
}
```

And the conditional read, where one outcome has no body at all:

```csharp
builder.Outcomes(o => o
    .StatusCode(StatusCodes.Status304NotModified, when: (r, ctx) => ctx.Request.Headers.IfNoneMatch == r.ETag)
    .Ok());
```

`304` is declared as `void` rather than dropped (the `docs/known-issues/054` behaviour), `SYNE003`
stays quiet because the success mapping is explicit, and no `OnSuccess` override is needed — so
`SYNE004` has nothing to warn about.

## Acceptance criteria

- [ ] Multiple success statuses appear in the OpenAPI document, each with the right body type or
      none (reuse the `docs/known-issues/054` fix — a bodyless status is declared as `void`, not
      dropped).
- [ ] Exactly one outcome is selected per request, and an unmatched response is a clear runtime
      error rather than a silent `200`.
- [ ] `SYNE003` no longer warns when outcomes are declared.
- [ ] `SYNE004` (`OnSuccess` conflicts with a declarative mapper) extended to the new surface.

## Notes

Sequence this after [001](001-openapi-failure-responses.md). Once an endpoint can declare arbitrary
responses, part of this becomes documentation rather than dispatch, and the remaining work is only
about *selecting* between outcomes.
