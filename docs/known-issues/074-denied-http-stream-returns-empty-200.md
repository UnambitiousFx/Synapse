# [Bug]: A denied stream request answers `200 OK` with an empty body over HTTP

**Severity:** **High**
**Area:** AspNetCore mapping
**Discovered on:** `main`, .NET 10, during an RFC-compliance audit of the web integration packages
**Status:** ✅ **Resolved** on `fix/aspnetcore-rfc-auth-responses` — see [Resolution](#resolution).

> **TL;DR.** `IHttpInvoker.InvokeStreamAsync` now returns `ValueTask<IResult>`: a failed first item is
> mapped like any other failure (401/403/404 Problem Details), and a failure after the 200 started aborts the
> response instead of being skipped. **Breaking change.**

---

## Describe the bug

`IHttpInvoker.InvokeStreamAsync` returned `IAsyncEnumerable<TItem>` and silently skipped failed items. Since
stream authorization (#175), a denied stream yields a single `UnauthenticatedFailure` / `UnauthorizedFailure`
item, so an anonymous or forbidden caller received `200 OK` with `[]`: a success status for a denied request,
indistinguishable from an empty result. A failure in the middle of a stream likewise produced a
complete-looking array with items missing.

---

## Steps to reproduce

1. Register `AddSynapseAuthorization()` and a stream request that is not `IAllowAnonymous`.
2. Map `invoker.InvokeStreamAsync(new ExportLoans(...), ct)` with `IHttpInvoker`.
3. Call it without credentials.

---

## Expected behavior

`401 Unauthorized` (or `403`) with a Problem Details body, as for a non-stream request.

---

## Actual behavior

`200 OK` with body `[]`.

---

## Code sample

```csharp
app.MapGet("/loans/export", (IHttpInvoker invoker, CancellationToken ct) =>
    invoker.InvokeStreamAsync(new ExportLoans(), ct)); // anonymous → 200 []
```

---

## Library version

`main`

## .NET version

.NET 10.0

## Operating system

macOS

---

## Additional context

### Root cause

An `IAsyncEnumerable<T>` return value cannot carry a status code: the endpoint always answers 200 and the
invoker could only drop failures.

### Resolution

`InvokeStreamAsync` now returns `ValueTask<IResult>`. The stream is opened only when that result executes (a
result an endpoint filter discards opens nothing), and the first item is read before choosing the response:

- first item failed → the failure goes through `IFailureHttpMapper` (with the 401 challenge of issue 073);
- stream empty → `200` with `[]`, still typed `IAsyncEnumerable<TItem>` so Native AOT needs one `JsonTypeInfo`;
- otherwise → `200` streaming the items as a JSON array. A later failure throws, which aborts the response so
  the client sees a truncated body rather than a complete one.

Minimal API lambdas that return the call unchanged keep compiling; code that `await foreach`-ed the result
must use `IInvoker.InvokeStreamAsync` instead.

**Verification.** `HttpInvokerTests.InvokeStreamAsync_WhenFirstItemIsDenied_ReturnsMappedFailure`,
`..._WhenAllItemsSucceed_Streams200JsonArray`, `..._WhenStreamIsEmpty_Returns200EmptyArray`,
`..._WhenItemFailsAfterResponseStarted_Throws`.
