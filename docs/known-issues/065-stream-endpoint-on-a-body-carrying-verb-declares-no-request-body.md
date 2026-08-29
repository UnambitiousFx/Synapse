# [Bug]: Stream endpoint on a body-carrying verb declares no request body

**Severity:** Medium
**Area:** AspNetCore mapping
**Discovered on:** `feat/synapse-endpoints`, .NET 10, while adding a `POST` stream to `examples/EndpointsApi`
**Status:** ✅ **Resolved** on `feat/synapse-endpoints` — see [Resolution](#resolution).

> **TL;DR.** `StreamEndpoint.CreatePlan` never called `Accepts<TRequest>`, so a `POST` stream bound its
> message from the JSON body while publishing no `requestBody` and letting a wrong content type through
> to the binder; the declaration is now emitted, guarded on the verb.

---

## Describe the bug

`StreamEndpoint<TRequest, TItem>` supports a body-carrying verb: `IStreamEndpointBuilder` offers
`Post`, and the generated binder for such an endpoint deserializes `TRequest` from the request body
exactly as the single-response tiers do. Its `CreatePlan` declared the negotiated `200` and a
`ProducesValidationProblem`, but never declared what the endpoint *accepts*.

Every other body-carrying tier does: `RawEndpoint<TRequest>`, `RawEndpoint<TRequest, TResponse>` and
`MappedEndpoint<…>` each call `Accepts` behind an `HttpMethodHelpers.AllVerbsAreBodyless` guard.
`StreamEndpoint` was the one that did not, so the helper written for exactly this decision was never
consulted from it.

Two things follow from the omission, and neither is visible at build time:

1. The OpenAPI operation has no `requestBody`, so a client generator models the route as taking no
   input and a reader cannot discover the shape the endpoint in fact requires.
2. Routing's consumes-matcher policy has nothing to match on, so a request with a wrong
   `Content-Type` is not rejected during routing. It reaches the binder, which answers `400` with a
   validation problem, where every other body-carrying endpoint answers `415 Unsupported Media Type`.

---

## Steps to reproduce

1. Declare a stream endpoint on a body-carrying verb:

   ```csharp
   public sealed record StreamSearchTasksQuery : IStreamRequest<TaskDto>
   {
       public required string Title { get; init; }
   }

   [Post("/tasks/stream/search")]
   public sealed class StreamSearchTasksEndpoint
       : StreamEndpoint<StreamSearchTasksQuery, TaskDto>;
   ```

2. Run the app and read `/openapi/v1.json`. The `post` operation for `/tasks/stream/search` has no
   `requestBody` member, while `POST /tasks` alongside it does.
3. `curl -X POST /tasks/stream/search -H 'Content-Type: text/plain' --data x` → `400`, where the same
   request against any other body-carrying endpoint is `415`.
4. `curl -X POST /tasks/stream/search -H 'Content-Type: application/json' --data '{"title":"a"}'` →
   `200` and the items stream, confirming the endpoint really does read the body it never declared.

---

## Expected behavior

A stream endpoint whose verb carries a body declares `Accepts<TRequest>("application/json")`, the same
as every other tier that reads one: the request schema reaches the OpenAPI document, and routing
rejects a wrong content type with `415` before the binder runs. A bodyless stream — the common
`GET` — declares nothing.

## Actual behavior

No `Accepts` at all, on either verb. The document omitted the input shape, and a wrong content type
produced a `400` from the binder rather than a `415` from routing.

---

## Code sample

```csharp
// src/Synapse.Endpoints/StreamEndpoint.cs — CreatePlan, before
ApplyMetadata = handlerBuilder =>
{
    // No Accepts call at all, on either verb.
    handlerBuilder.WithMetadata(new ProducesResponseMetadata(
        StatusCodes.Status200OK,
        typeof(IAsyncEnumerable<TItem>),
        ["application/json", "text/event-stream"]));
    handlerBuilder.ProducesValidationProblem();
    plan.ApplyMetadata(handlerBuilder);
}
```

---

## Library version

`feat/synapse-endpoints`

## .NET version

.NET 10.0

## Operating system

macOS (Darwin 25.6.0, arm64)

---

## Additional context

### Root cause

`StreamEndpoint` derives from `RawEndpoint` rather than from `RawEndpoint<TRequest, TResponse>`,
because it writes the body itself instead of returning a single value to serialize. That inheritance
choice is right, but it also meant `CreatePlan` was written from scratch rather than inherited — and
the `Accepts` declaration, which lives in each tier's own `ApplyMetadata`, was simply not carried
over. `HttpMethodHelpers.AllVerbsAreBodyless` already existed and already documented this exact
decision; nothing referenced it from this file.

The gap stayed invisible because the endpoint works: the binder reads the body regardless of what the
metadata says, so every test that exercised a `POST` stream over `application/json` passed. Only the
document and the wrong-content-type path showed it, and neither was asserted.

### Resolution

`StreamEndpoint.CreatePlan` now declares `Accepts<TRequest>("application/json")` behind the same
`AllVerbsAreBodyless` guard the other tiers use, so a body-carrying stream declares its input and a
bodyless one still declares nothing.

```csharp
if (!HttpMethodHelpers.AllVerbsAreBodyless(plan.HttpMethods))
{
    handlerBuilder.Accepts<TRequest>("application/json");
}
```

**Verification.** Two unit tests in `test/Synapse.Endpoints.Tests/OpenApiMetadataTests.cs` assert
`IAcceptsMetadata` is present with `RequestType` of `TRequest` for a `POST` stream and absent for a
`GET` one. Three integration tests in `examples/EndpointsApi.Tests/StreamSearchTests.cs` assert the
`requestBody` with its schema is in `/openapi/v1.json` for `POST /tasks/stream/search`, that
`GET /tasks/stream` still has none, and that a `text/plain` body now answers `415`. All 125
`Synapse.Endpoints` tests and all 42 `EndpointsApi` integration tests pass, and the Native AOT publish
of `examples/EndpointsApi` stays free of IL/RDG warnings.
