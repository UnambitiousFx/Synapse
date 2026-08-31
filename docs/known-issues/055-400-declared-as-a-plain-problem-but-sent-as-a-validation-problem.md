# [Bug]: The `400` is declared as a plain problem but sent as a validation problem

**Severity:** Medium
**Area:** AspNetCore mapping (`src/Synapse.Endpoints`)
**Discovered on:** `feat/synapse-endpoints`, .NET 10, reviewing the low-level endpoint tier
**Status:** ✅ **Resolved** on `feat/synapse-endpoints` — see [Resolution](#resolution).

> **TL;DR.** Accumulating binders changed every bind failure from `TypedResults.Problem(detail, 400)` to
> `TypedResults.ValidationProblem(errors)`, so the body gained an `errors` dictionary. All four endpoint
> shapes still declared `ProducesProblem(400)`, whose schema is a plain `ProblemDetails` with no
> `errors` member — so the document described a narrower body than the endpoint sends. It was accurate
> before this branch and inaccurate after it, which makes this a regression rather than an old gap.

---

## Describe the bug

Binding failures used to carry one message:

```csharp
await TypedResults.Problem(bound.Error, statusCode: StatusCodes.Status400BadRequest)
    .ExecuteAsync(context);
```

They now carry every failure, keyed by field:

```csharp
return bound.Problem();     // -> TypedResults.ValidationProblem(errors)
```

`ValidationProblem` writes `HttpValidationProblemDetails`, which is `ProblemDetails` plus
`errors: { field: [message, …] }`. The declared metadata did not move with it: all four shapes —
`RawEndpoint<TRequest, TResponse>`, `RawEndpoint<TRequest>`, `MappedEndpoint<…>` and
`StreamEndpoint<…>` — still called `ProducesProblem(StatusCodes.Status400BadRequest)`.

A consumer generating a client from the document therefore gets a `400` type with `type`, `title`,
`status`, `detail` and `instance`, and no way to reach the `errors` dictionary that is the entire point
of the accumulating binder. The mismatch is invisible from inside the library: both are
`application/problem+json`, both validate as problem documents, and only the schema differs.

---

## Steps to reproduce

1. Run `examples/EndpointsApi`.
2. `GET /reports?page=0` and note the `errors` object in the body.
3. `GET /openapi/v1.json` and look at `paths["/reports"].get.responses["400"]`.

---

## Expected behavior

```json
"400": { "content": { "application/problem+json": {
  "schema": { "$ref": "#/components/schemas/HttpValidationProblemDetails" } } } }
```

with `errors` among that schema's properties.

---

## Actual behavior

```json
"400": { "content": { "application/problem+json": {
  "schema": { "$ref": "#/components/schemas/ProblemDetails" } } } }
```

while the response body is

```json
{ "type": "…", "title": "One or more validation errors occurred.", "status": 400,
  "errors": { "page": ["must be at least 1"], "tag": ["at least one tag is required"] } }
```

---

## Code sample

```csharp
// Declared (before):
handlerBuilder.ProducesProblem(StatusCodes.Status400BadRequest);

// Sent:
return bound.Problem();     // HttpValidationProblemDetails, with errors

// Declared (after):
handlerBuilder.ProducesValidationProblem();
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

The change of response shape and the declaration of that shape live in two different places in the same
method — the `InvokeAsync`/`HandleAsync` body and the `ApplyMetadata` callback — and only one of them
was updated when `BindResult<T>` was reshaped. Nothing ties them together: `ProducesProblem` and
`ValidationProblem` are unrelated APIs, and no test compared the declared schema against a real
response body.

Worth recording because the failure mode is asymmetric: getting the response *shape* wrong is loud (a
client fails to deserialize), while getting the *declaration* wrong is silent until someone generates a
client from the document and discovers a field they cannot read.

### Resolution

All four shapes now declare `ProducesValidationProblem()`, whose default status code is already `400`
and whose content type is already `application/problem+json`. It is the same kind of metadata-only
extension as `ProducesProblem` — no `WithOpenApi`, no reflection — so the AOT posture is unchanged, and
the natively published binary was checked rather than assumed.

**Verification.** `OpenApiMetadataTests.CreateDescriptor_DeclaresTheValidationProblemItActuallySendsForA400`
asserts the declared `400` is `typeof(HttpValidationProblemDetails)` with
`application/problem+json`. End to end: the natively published `examples/EndpointsApi` serves a document
whose `/reports` `400` references `HttpValidationProblemDetails`, that schema does carry `errors`, and
the endpoint's real `400` body matches it. The `endpoints-native-aot` CI job now asserts the `$ref`, and
that assertion was checked against a doctored document to confirm it fails when the schema is wrong.
