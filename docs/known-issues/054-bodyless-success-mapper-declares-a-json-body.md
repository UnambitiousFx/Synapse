# [Bug]: A bodyless success mapper declares a JSON body it never sends

**Severity:** Medium
**Area:** AspNetCore mapping (`src/Synapse.Endpoints`)
**Discovered on:** `feat/synapse-endpoints`, .NET 10, reviewing the low-level endpoint tier
**Status:** ✅ **Resolved** on `feat/synapse-endpoints` — see [Resolution](#resolution).

> **TL;DR.** `builder.NoContent()` and `builder.StatusCode(int)` install a success mapper that writes a
> status line and nothing else, but the endpoint base declared `typeof(TResponse)` for whatever status
> code they set. So a `204` arrived in `/openapi/v1.json` carrying an `application/json` schema for a
> body that never comes — invalid for a `204`, and a generated client models a return value it will
> never receive. The response type is now declared only when the configured mapper actually writes one.

---

## Describe the bug

`EndpointBuilder<TResponse>` offers two declarative mappers that produce no body:

```csharp
builder.NoContent();                                   // 204, no body
builder.StatusCode(StatusCodes.Status304NotModified);  // 304, no body
```

Both set `DeclaredSuccessStatusCode`, which the endpoint base reads so the document shows the real
status code instead of assuming `200`. But the base declared the response *type* unconditionally:

```csharp
new ProducesResponseMetadata(SuccessStatusCode(configuration), typeof(TResponse))
```

`DeclaredSuccessStatusCode` alone cannot distinguish these mappers from `Ok()`, `Created()` and
`Accepted()`, which set it too and *do* write a body. The result was a declared response whose status
code was right and whose content was fiction.

This is the other half of the defect [051](051-declared-bodyless-responses-never-reach-the-openapi-document.md)
set out to fix. 051 handled "no type supplied" — a bodyless response described with a null `Type`,
which `Microsoft.AspNetCore.OpenApi` skipped outright. It did not touch the opposite case: a bodyless
response for which a type *was* supplied, from a caller that had no way to say the mapper writes
nothing. Both endpoints of the same question, fixed one at a time.

Affected `RawEndpoint<TRequest, TResponse>` (and therefore `Endpoint<TRequest, TResponse>`) and
`MappedEndpoint<…>`. The void arity was never affected: it passes no type at all.

---

## Steps to reproduce

1. Add `.NoContent()` to the `Configure` of any `Endpoint<TRequest, TResponse>` — for example
   `SearchTasksEndpoint` in `examples/EndpointsApi`.
2. Run the app and `GET /openapi/v1.json`.
3. Compare that endpoint's `204` with what the endpoint actually returns.

---

## Expected behavior

```json
"204": { "description": "No Content" }
```

---

## Actual behavior

```json
"204": {
  "description": "No Content",
  "content": {
    "application/json": { "schema": { "type": "array", "items": { "$ref": "…/TaskDto" } } }
  }
}
```

while the endpoint answers `HTTP/1.1 204 No Content` with an empty body.

---

## Code sample

```csharp
[Get("/tasks/search")]
public sealed class SearchTasksEndpoint : Endpoint<SearchTasksQuery, IReadOnlyList<TaskDto>>
{
    public override void Configure(IEndpointBuilder<IReadOnlyList<TaskDto>> builder)
    {
        builder.NoContent();   // writes a status line and nothing else
    }
}

// Declared before the fix: ProducesResponseMetadata { 204, Type = IReadOnlyList<TaskDto>,
//                                                     ContentTypes = ["application/json"] }
// Declared after:          ProducesResponseMetadata { 204, Type = void, ContentTypes = [] }
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

`EndpointConfiguration<TResponse>` recorded *which* status code the declarative mapper set, but not
whether that mapper writes a body. The endpoint base had `typeof(TResponse)` to hand and no reason not
to use it, so it always did. Nothing in the type system connects "this mapper is
`_ => TypedResults.NoContent()`" to "there is no response body to describe".

### Resolution

`EndpointConfiguration<TResponse>` gained `SuccessResponseHasBody`, set to `false` by `NoContent()` and
`StatusCode(int)` and back to `true` by `Ok()`, `Created()` and `Accepted()` — so it stays correct if a
`Configure` calls more than one of them. The endpoint bases declare the response type only when it is
set:

```csharp
new ProducesResponseMetadata(
    SuccessStatusCode(configuration),
    configuration.SuccessResponseHasBody ? typeof(TResponse) : null)
```

`ProducesResponseMetadata` already normalises a null type to `void` with empty content types, which is
what 051 established, so nothing further was needed to make the entry appear correctly.

**Verification.** `OpenApiMetadataTests.CreateDescriptor_ForABodylessSuccessMapper_DeclaresNoResponseBody`
covers `NoContent()` and `StatusCode(304)` as a theory and asserts the declaration is `void` with no
content types; its counterpart
`CreateDescriptor_ForASuccessMapperWithABody_StillDeclaresTheResponseType` asserts `Created()` still
declares `typeof(TResponse)` with `application/json`, so the fix did not simply stop declaring response
types. Both assert on the library's own `ProducesResponseMetadata` rather than on every
`IProducesResponseTypeMetadata` the endpoint carries, because what the framework infers from the mapping
lambda differs by target framework. The `endpoints-native-aot` CI job now asserts that no declared
bodyless response carries a content schema, checked against the document served by the natively
published binary.
