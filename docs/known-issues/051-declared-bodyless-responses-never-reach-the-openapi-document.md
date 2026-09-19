# [Bug]: Declared bodyless responses never reach the OpenAPI document

**Severity:** Medium
**Area:** AspNetCore mapping (`src/Synapse.Endpoints`)
**Discovered on:** `feat/synapse-endpoints`, .NET 10, while adding the low-level endpoint tier
**Status:** ✅ **Resolved** on `feat/synapse-endpoints` — see [Resolution](#resolution).

> **TL;DR.** `ProducesResponseMetadata` described a response with no body by leaving `Type` as
> `null`, and `Microsoft.AspNetCore.OpenApi` skips an `IProducesResponseTypeMetadata` whose `Type` is
> null outright. So `Endpoint<TRequest>`'s `204 No Content` — the whole point of that arity — was
> declared in metadata, asserted by a unit test, and absent from `/openapi/v1.json`. The type is now
> `typeof(void)`, with content types still empty.

---

## Describe the bug

`Endpoint<TRequest>` declares its success status through
`handlerBuilder.WithMetadata(new ProducesResponseMetadata(204))`, and `ProducesResponseMetadata` set
`Type = null` for a response with no body. That reads correctly — there is no body, so there is no
type — but `Microsoft.AspNetCore.OpenApi`'s schema generation ignores such an entry entirely rather
than emitting a response with no content.

The result: every `PUT` and `DELETE` written as `Endpoint<TRequest>` documented only its `400`. The
`204` it actually returns on the happy path was missing from the document, so a generated client had
no successful response to model for the most common command shape in the library.

This survived the whole-branch review because the unit test that covers it,
`OpenApiMetadataTests.CreateDescriptor_ForEndpointWithNoResponse_Declares204NotDefault200`, asserts on
the *metadata collection* rather than on a produced document — and the metadata was correct. It even
asserted `metadata.Type is null` explicitly, using exactly the property that caused the omission as
the way to distinguish the library's entry from the framework's inferred `200, System.Void` one. The
integration test that parses `/openapi/v1.json` checked the `GET` and `POST` paths, both of which have
typed responses and were therefore unaffected.

Found while adding `IRawEndpointBuilder.Produces(int)` for the low level: a `Produces(304)` on a
conditional-GET endpoint did not appear either, which is what prompted looking at the shared
metadata type.

---

## Steps to reproduce

1. Run `examples/EndpointsApi`.
2. `GET /openapi/v1.json`.
3. Look at `paths["/tasks/{taskId}"].put.responses`.

---

## Expected behavior

```json
{ "204": { "description": "No Content" }, "400": { … } }
```

---

## Actual behavior

```json
{ "400": { … } }
```

The `204` is absent. Same for `DELETE /tasks/{taskId}`, and same for any
`builder.NoContent()` / `builder.StatusCode(…)` declaration that produces no body.

---

## Code sample

```csharp
[Put("/tasks/{taskId:guid}")]
[InGroup<TasksGroup>]
public sealed class UpdateTaskEndpoint : Endpoint<UpdateTaskCommand>;

// Metadata attached (correct, and unit-tested):
//     ProducesResponseMetadata { StatusCode = 204, Type = null, ContentTypes = [] }
//
// /openapi/v1.json before the fix:
//     "put": { "responses": { "400": { … } } }
//
// after:
//     "put": { "responses": { "204": { … }, "400": { … } } }
```

---

## Library version

`feat/synapse-endpoints`

## .NET version

.NET 10.0

## Operating system

macOS (platform-independent)

---

## Additional context

### Root cause

`ProducesResponseMetadata`'s constructor:

```csharp
Type = type;
ContentTypes = type is null ? [] : contentTypes ?? JsonContentType;
```

A null `Type` is a valid `IProducesResponseTypeMetadata` as far as the interface is concerned, and the
library's own OpenAPI-metadata unit tests were satisfied by it. But
`Microsoft.AspNetCore.OpenApi` treats a null `Type` as "nothing to describe" and drops the whole
response entry, not just its schema. The framework's own inferred entry for a `Task`-returning
delegate uses `typeof(void)`, not null — which is exactly the convention that works.

### Resolution

`ProducesResponseMetadata` now normalises a bodyless response to `typeof(void)` while keeping its
content types empty:

```csharp
Type = type ?? typeof(void);
ContentTypes = type is null || type == typeof(void) ? [] : contentTypes ?? JsonContentType;
```

Nothing else changed: callers still pass no type for a bodyless response. This also makes the new
`IRawEndpointBuilder.Produces(int)` work, which is why the low-level builder can offer it at all — a
declaration method that silently produced nothing would have been the same kind of trap that
`NoContent()` and `StatusCode(int)` were deliberately left off that interface for.

**Verification.** `OpenApiMetadataTests.CreateDescriptor_ForEndpointWithNoResponse_Declares204NotDefault200`
now asserts `Type == typeof(void)`, with a comment recording why null was wrong.
`RawEndpointsTests.GetOpenApi_DocumentsBodylessResponses` parses the real document from
`examples/EndpointsApi` and asserts the `204` on both `PUT` and `DELETE /tasks/{taskId}` — it fails
against the previous behaviour. `RawEndpointsTests.GetOpenApi_DocumentsTheLowLevelEndpointsDeclaredContract`
covers the low-level `304`. The `endpoints-native-aot` CI job asserts both from the natively published
binary, so the document is checked where the schema generator has no reflection to fall back on.
