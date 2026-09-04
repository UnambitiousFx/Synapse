# 018 — OpenAPI parameter metadata

|  |  |
|---|---|
| **Status** | 🔴 Missing |
| **Priority** | Medium |
| **Area** | OpenAPI / Builders |
| **Tiers** | All bound tiers |
| **Breaking** | No — additive |

## Problem

Synapse declares no query or header parameter in the OpenAPI document at all today, scalar or array.
`?page=2`, `?tag=a&tag=b`, an `X-Tenant` header read by `[FromHeader]` — none of them appear in
`/openapi/v1.json`. A generated client sees a route with hidden, undocumented inputs; a human reading
the document has no way to know a query string is expected without reading the handler.

A form-bound message has the mirror gap on the request body side: the endpoint declares
`multipart/form-data` and `application/x-www-form-urlencoded` as accepted content types, but no
schema for either — no field names, no indication a `file` part is expected alongside a `caption`
field. Both gaps were found while shipping features 006 (form and file binding) and 007 (collection
binding), each of which lists an OpenAPI acceptance criterion that could not be met without this work
— see their own [Acceptance criteria](./006-form-and-file-binding.md) sections, which point back
here instead of standing as unchecked boxes with no explanation.

## Current state

- `src/Synapse.Endpoints/Builders/EndpointBuilderCore.cs` emits exactly two kinds of OpenAPI
  metadata: `ProducesResponseMetadata` (via `Produces`/`ProducesProblem`/`ProducesValidationProblem`)
  and, through `RequestBodyMetadata.Apply` (`src/Synapse.Endpoints/Internal/RequestBodyMetadata.cs`),
  either `Accepts(requestType, "application/json")` for a JSON-bound message or the custom
  `FormRequestMetadata` (`src/Synapse.Endpoints/Internal/FormRequestMetadata.cs`) for a form-bound
  one. Neither path touches a query, header, route or form-field parameter.
- Grepping the whole `src/` tree for `OpenApiParameter` — the type `Microsoft.AspNetCore.OpenApi`
  reads to describe a non-body input — returns nothing. Route parameters appear in the generated
  document only because ASP.NET Core's own route-template parsing infers them independently of
  anything this package emits; a query key, a header or a form field never goes through that path.
- `FormRequestMetadata.RequestType` is deliberately `null` (see its own remarks) precisely because a
  message containing `IFormFile` is not describable as a JSON schema — passing the message type
  through the ordinary `Accepts<T>` path would hand the schema generator a type it cannot render.
  Declaring the correct content types without a schema is what unblocked features 006 and 007; it did
  not, and could not on its own, produce the schema.

## What you cannot write today

A search endpoint whose query parameters are entirely real, and entirely invisible in the document:

```csharp
public sealed record SearchTasksQuery : IRequest<IReadOnlyList<TaskDto>>
{
    public required int Page { get; init; }
    [FromQuery(Name = "tag")] public string[] Tags { get; init; } = [];
}

[Get("/tasks/search")]
public sealed class SearchTasksEndpoint : Endpoint<SearchTasksQuery, IReadOnlyList<TaskDto>>;
```

```json title="/openapi/v1.json — what is actually there today"
"get": {
  "parameters": [],
  "responses": { "200": { "…": "…" }, "400": { "…": "…" } }
}
```

No `page`, no `tag`, no indication either one exists, is required, or repeats. A generated TypeScript
or C# client has no field for either — the caller finds out `page` is required only from the `400`
that comes back the first time they omit it.

The form side of the same gap, from feature 006's shipped endpoint:

```csharp
public sealed record UploadAttachmentCommand : IRequest<AttachmentCreated>
{
    public required Guid TaskId { get; init; }
    public required IFormFile File { get; init; }
    public required string Caption { get; init; }
}
```

```json title="/openapi/v1.json — what is actually there today"
"post": {
  "requestBody": {
    "content": {
      "multipart/form-data": {},
      "application/x-www-form-urlencoded": {}
    }
  }
}
```

Both content types are correctly declared — the point features 006 and 007 actually needed, and the
reason neither is blocked on this doc — but a client generator sees an empty schema for each: nothing
says a `file` part and a `caption` field belong in this request. There is no way, at any tier, to add
that description yourself; `Raw`'s `AddOpenApiOperationTransformer` (see
[OpenAPI → Adding anything else](../../docs/endpoints/reference/openapi.mdx#adding-anything-else))
can mutate an already-generated operation by hand, but nothing in the pipeline computes the parameter
or field list for it to start from.

## Proposed API

Two shapes, both additive to `EndpointBuilderCore` and both consumed the same way `ProducesResponseMetadata`
already is — through a custom metadata type `Microsoft.AspNetCore.OpenApi` reads at document-generation
time, not through the framework's own `Accepts`/route-parameter inference, which cannot express either
one:

```csharp
// One per route/query/header parameter the generator resolves for this endpoint's message.
internal sealed class BoundParameterMetadata : IParameterMetadata
{
    public string Name { get; }
    public ParameterLocation Location { get; }   // Query, Header, Path
    public bool Required { get; }
    public bool IsArray { get; }
    public Type ElementType { get; }
}

// One per form field or file the generator resolves for a form-bound message.
internal sealed class FormFieldMetadata : IAcceptsMetadata
{
    // As FormRequestMetadata today, plus a per-field name/type/required list the document can
    // render as the multipart/urlencoded schema instead of an empty one.
}
```

Generator work: `CollectBindableProperties`'s already-resolved `BindablePropertyModel` list is the
exact input both need — it already knows each property's source, key, nullability and shape, which is
everything a parameter or a field-schema entry requires. The emitter attaches one `BoundParameterMetadata`
per route/query/header-bound property (skipping `[FromBody]` and form-bound ones, which are body
concerns) and one `FormFieldMetadata` entry per form-bound property, alongside the existing
`RequestBodyMetadata.Apply` call.

### With the proposal

The same `SearchTasksQuery` endpoint, unchanged:

```json title="/openapi/v1.json"
"get": {
  "parameters": [
    { "name": "Page", "in": "query", "required": true,
      "schema": { "type": "integer", "format": "int32" } },
    { "name": "tag", "in": "query", "required": false,
      "schema": { "type": "array", "items": { "type": "string" } } }
  ],
  "responses": { "200": { "…": "…" }, "400": { "…": "…" } }
}
```

The same `UploadAttachmentCommand` endpoint, unchanged:

```json title="/openapi/v1.json"
"post": {
  "requestBody": {
    "content": {
      "multipart/form-data": {
        "schema": { "type": "object", "properties": {
          "file": { "type": "string", "format": "binary" },
          "caption": { "type": "string" }
        }, "required": ["file", "caption"] } }
    }
  }
}
```

Neither example changes a line of the endpoint's own C#. The document catches up to what the binder
already knows; nothing about the binding rules or the generated code changes.

## Acceptance criteria

- [ ] A route-bound property appears as a `path` parameter — today inferred correctly by ASP.NET Core
      from the route template alone, so this criterion is about not regressing that, not about new work.
- [ ] A query-bound scalar property appears as a `query` parameter with the right `required` and
      `schema`.
- [ ] A query- or header-bound collection property (feature 007) appears as an array parameter with
      the right element schema.
- [ ] A header-bound property appears as a `header` parameter.
- [ ] A form-bound message (feature 006) declares a `multipart/form-data` and
      `application/x-www-form-urlencoded` schema naming every form field and file, instead of an
      empty content-type entry.
- [ ] `net8.0` is unaffected — this work extends the same `net9.0`+-only OpenAPI integration
      features 001 and the rest already depend on; see
      [OpenAPI → `net8.0` has no document integration](../../docs/endpoints/reference/openapi.mdx#net80-has-no-document-integration).
- [ ] Tests assert on the generated `OpenApiDocument` directly (the existing `OpenApiMetadataTests`
      pattern), not on string-matched JSON, so a schema shape regression fails loudly.
