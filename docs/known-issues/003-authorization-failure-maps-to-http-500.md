# [Bug]: Pipeline short-circuit via `Result.Failure<T>()` returns HTTP 500 instead of 403/401 — ✅ RESOLVED

**Severity:** Medium  
**Status:** ✅ Resolved on `feature/typed-pipeline-behaviors` — see [Resolution](#resolution).  
**Area:** `Synapse.AspNetCore` / `UnambitiousFx.Functional.AspNetCore` (cross-repo)  
**Discovered on:** `feature/typed-pipeline-behaviors`, .NET 10

---

## Describe the bug

A pipeline behavior that short-circuits an unauthorized request by returning
`Result.Failure<TResponse>("Forbidden: ...")` causes the HTTP endpoint to respond with
**`500 Internal Server Error`** and a ProblemDetails body, rather than the semantically correct
**`403 Forbidden`** (or `401 Unauthorized`).

The short-circuit mechanism itself works correctly — the handler never executes (verified by
server logs) — but the HTTP status code is wrong. This affects any behavior that uses
`Result.Failure` to signal authorization or access-control denials.

---

## Steps to reproduce

1. Implement a pipeline behavior that denies requests and short-circuits with `Result.Failure`:

   ```csharp
   public sealed class AuthorizationBehavior<TRequest, TResponse>
       : IRequestPipelineBehavior<TRequest, TResponse>
       where TRequest : IRequest<TResponse>, ISecuredRequest
       where TResponse : notnull
   {
       public async ValueTask<Result<TResponse>> HandleAsync(
           TRequest request,
           RequestHandlerDelegate<TResponse> next,
           CancellationToken ct = default)
       {
           var hasPermission = /* check header / claims */;
           if (!hasPermission)
               return Result.Failure<TResponse>($"Forbidden: requires permission '{request.RequiredPermission}'");

           return await next(request, ct);
       }
   }
   ```

2. Register the behavior and a protected endpoint:

   ```csharp
   cfg.AddOpenGenericRequestWithResponsePipelineBehavior(typeof(AuthorizationBehavior<,>));

   app.MapPost("/admin/purge", async (IHttpInvoker invoker, CancellationToken ct) =>
       await invoker.InvokeAsync(new PurgeCompletedTasksCommand(), ct));
   ```

3. Call `POST /admin/purge` **without** the required permission header.

---

## Expected behavior

The endpoint returns **`403 Forbidden`** (or `401 Unauthorized`), indicating that the request was
understood but deliberately denied.

---

## Actual behavior

The endpoint returns **`500 Internal Server Error`** with a ProblemDetails body:

```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.6.1",
  "title": "Internal Server Error",
  "status": 500,
  "detail": "Forbidden: requires permission 'tasks:admin'"
}
```

The server logs confirm the short-circuit worked (no handler log line appears), so the behavior
logic is correct — only the HTTP status mapping is wrong.

Expected log (no `🧹 PURGING` line = handler never ran):
```
warn: AuthorizationBehavior  🚫 [auth] PurgeCompletedTasksCommand denied — requires 'tasks:admin'
info: MetricsBehavior        ◀ [metrics:10] PurgeCompletedTasksCommand finished in 00:00:00.003
```

---

## Code sample

```csharp
// AuthorizationBehavior returns Result.Failure ← correct
return Result.Failure<TResponse>($"Forbidden: requires permission '{required}'");

// HttpInvoker calls AsHttpBuilder which delegates to DefaultFailureHttpMapper
public async ValueTask<IResult> InvokeAsync<TResponse>(IRequest<TResponse> request,
    CancellationToken cancellationToken = default) where TResponse : notnull
{
    return await _invoker.InvokeAsync(request, cancellationToken)
        .AsHttpBuilder(_failureMapper);  // DefaultFailureHttpMapper maps ALL generic failures → 500
}
```

---

## Library version

`feature/typed-pipeline-behaviors` (pre-release)  
`UnambitiousFx.Functional.AspNetCore` 1.0.6

## .NET version

.NET 10.0

## Operating system

Windows 11

---

## Additional context

### Root cause

`IHttpInvoker.InvokeAsync` routes failures through `IFailureHttpMapper`
(`src/Synapse.AspNetCore/ServiceCollectionExtensions.cs:23`):

```csharp
services.TryAddSingleton<IFailureHttpMapper, DefaultFailureHttpMapper>();
```

`DefaultFailureHttpMapper` is implemented in the external
**`UnambitiousFx.Functional.AspNetCore`** package. Its default mapping strategy returns
**`500 Internal Server Error`** for any failure that is not recognised as a validation error (the
only special-cased category, which produces `422 Unprocessable Entity`). There is no category for
authorization failures, so they fall through to the 500 default.

### To address

**Option A — typed/categorized failures (preferred, cross-repo):**  
Introduce a typed failure category in `UnambitiousFx.Functional` (e.g., `AuthorizationFailure`
or a status-carrying `Failure` with an `HttpStatusCode` hint) and update `DefaultFailureHttpMapper`
to recognise it and return `403`. Behaviors would then return:

```csharp
return Result.Failure<TResponse>(new AuthorizationFailure($"Requires permission '{required}'"));
```

This requires changes in the `UnambitiousFx.Functional` / `UnambitiousFx.Functional.AspNetCore`
repositories.

**Option B — example custom `IFailureHttpMapper` (short-term, in-repo):**  
Ship a `ExampleFailureHttpMapper` in `examples/MinimalApi` that inspects the failure message
prefix (`"Forbidden:"`) and returns `403`. Register it before `AddSynapseAspNetCore()`:

```csharp
builder.Services.AddSingleton<IFailureHttpMapper, ExampleFailureHttpMapper>();
builder.Services.AddSynapseAspNetCore(); // TryAddSingleton → no-op because already registered
```

This is a workaround, not a fix, but it demonstrates the extension point to users and makes the
example behave correctly.

**Option C — document as known limitation:**  
Add a note to the pipeline-behavior documentation explaining that `Result.Failure` maps to 500 by
default and that callers needing specific status codes should provide a custom `IFailureHttpMapper`.

> **Note:** at the time, the assertion in `examples/MinimalApi/Http/pipeline-behaviors.http` was
> `client.assert(response.status >= 400, ...)`, which passed for both 500 and 403, so the HTTP file demo
> was not broken — but the status code was semantically misleading for a real-world authorization
> scenario. It now asserts `response.status === 401` exactly.

---

## Resolution

Fixed in-repo, no cross-repo change required. The `UnambitiousFx.Functional` /
`UnambitiousFx.Functional.AspNetCore` packages were upgraded from **1.0.6** to **2.0.3** (since bumped
further — `Directory.Packages.props` is the source of truth), which ship the typed-failure
infrastructure that "Option A" called for:

- **Typed failures** in `UnambitiousFx.Functional.Failures` — `UnauthorizedFailure`,
  `UnauthenticatedFailure`, `NotFoundFailure`, `ConflictFailure`, `ValidationFailure`, … — plus
  factory extensions on `Result` (`FailUnauthorized`, `FailNotFound`, `FailConflict`, `FailValidation`, …).
- **`DefaultFailureHttpMapper`** (already registered by `AddSynapseAspNetCore`) now recognises each
  typed failure and maps it to the appropriate HTTP status instead of falling through to 500.

`AuthorizationBehavior` (`examples/MinimalApi/Infrastructure/Pipelines/AuthorizationBehavior.cs`) now
returns a **typed** failure rather than a string failure:

```csharp
// before — string failure, no category → mapped to 500
return Result.Failure<TResponse>($"Forbidden: requires permission '{required}'");

// after — typed authorization failure → mapped to a real denial status
return Result.FailUnauthorized<TResponse>($"Requires permission '{required}'");
```

### Resulting status code

`UnauthorizedFailure` maps to **`401 Unauthorized`** under the current `DefaultFailureHttpMapper`
(verified by `Purge_WithoutPermission_IsDenied` in `examples/MinimalApi.Tests/TasksApiTests.cs`).
This satisfies the expected behavior above ("403 Forbidden **or** 401 Unauthorized") — the request is
understood and deliberately denied, and is no longer a misleading 500. The package does not currently
expose a distinct `Forbidden` (403) failure category.

### Getting a specific status code (e.g. 403)

Callers who need a status the default mapper does not emit (such as a strict 403 for
authenticated-but-unpermitted callers) register a custom `IFailureHttpMapper` **before**
`AddSynapseAspNetCore()` — the registration uses `TryAddSingleton`, so the custom mapper wins:

```csharp
builder.Services.AddSingleton<IFailureHttpMapper>(
    new CompositeFailureHttpMapper(
        new TypedFailureHttpMapper<UnauthorizedFailure>(
            f => new FailureHttpResponse { StatusCode = StatusCodes.Status403Forbidden, Body = f.Message }),
        DefaultFailureHttpMapper.Instance));
builder.Services.AddSynapseAspNetCore(); // TryAddSingleton → no-op, custom mapper already registered
```
