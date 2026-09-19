# [Bug]: A request body with no `Content-Type` returns 500 with an unhandled-exception log line

**Severity:** Medium
**Area:** AspNetCore mapping (`src/Synapse.Endpoints`)
**Discovered on:** `feat/synapse-endpoints`, .NET 10, whole-branch review
**Status:** ✅ **Resolved** on `feat/synapse-endpoints` — see [Resolution](#resolution).

> **TL;DR.** `ReadJsonBodyAsync` caught only `JsonException`, but `ReadFromJsonAsync` throws
> `InvalidOperationException` for a non-JSON content type, so a body sent with no `Content-Type`
> escaped as a 500; the content type is now checked with `HasJsonContentType()` first and a malformed
> request gets its 400.

---

## Describe the bug

`BindingHelpers.ReadJsonBodyAsync<T>` wrapped `context.Request.ReadFromJsonAsync(typeInfo, …)` in a
`catch (JsonException)`, which covers a malformed payload but not a wrong or absent content type —
`HttpRequestJsonExtensions.ThrowContentTypeError` throws `InvalidOperationException`.

A *wrong but present* content type never reaches the binder: the `Accepts<TRequest>` metadata the
endpoint declares installs ASP.NET's consumes matcher policy, which rejects `text/plain` with `415`
during routing. An **absent** content type is different — a request with no `Content-Type` matches an
endpoint regardless of what it declares it accepts — so that one request shape reached the binder and
escaped the catch, producing `500` plus an `An unhandled exception has occurred while executing the
request` log line for every occurrence. A malformed request from a client should never be a 500.

---

## Steps to reproduce

1. POST a JSON body to any endpoint with a body-carrying verb, with the `Content-Type` header removed.

```bash
curl -i -X POST http://localhost:5000/tasks -H 'Content-Type:' -d '{"title":"x"}'
```

---

## Expected behavior

`400 Bad Request` with a `ProblemDetails` body naming the problem.

---

## Actual behavior

`500 Internal Server Error`, and an unhandled-exception log entry per request:
`System.InvalidOperationException: Unable to read the request as JSON because the request content type
'' is not a known JSON content type.`

---

## Code sample

```csharp
// Before: only JsonException was caught, so a non-JSON content type escaped.
try
{
    var value = await context.Request.ReadFromJsonAsync(typeInfo, context.RequestAborted);
    ...
}
catch (JsonException exception) { ... }
```

---

## Library version

`feat/synapse-endpoints` (pre-release; `Synapse.Endpoints` not yet published)

## .NET version

.NET 10.0

## Operating system

macOS (Darwin), reproducible on any platform

---

## Additional context

### Root cause

The failure taxonomy the helper implemented (empty body, malformed JSON) had no entry for "the body is
not being offered as JSON at all", and the one exception type it caught did not cover it.

### Resolution

Added a `context.Request.HasJsonContentType()` guard ahead of the read, returning a
`BindResult<T>.Failure` that names the content type actually received. The endpoint's existing failure
path turns that into the `400` with `ProblemDetails` that a malformed client request deserves. The
`Content-Length == 0` short-circuit stays ahead of it, so a genuinely bodyless request keeps the more
specific "required but was empty" message.

**Verification.** Unit tests for an absent content type, `text/plain`, and the three JSON content
types that must still be read (`application/json`, `application/json; charset=utf-8`,
`application/problem+json`); an end-to-end test
(`TaskEndpointsTests.Post_WithABodyButNoContentType_Returns400NotAnUnhandledException`); and checks in
the Native AOT smoke test for both the `400` and the `415` that must still happen, so the guard cannot
be mistaken for having replaced content-type rejection with a blanket 400.
