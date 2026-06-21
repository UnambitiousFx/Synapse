# [Bug]: Pipeline short-circuit via `Result.Failure<T>()` returns HTTP 500 instead of 403/401

**Severity:** Medium  
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

> **Note:** the `client.assert(response.status >= 400, ...)` assertion in
> `examples/MinimalApi/Http/pipeline-behaviors.http` passes for both 500 and 403, so the HTTP
> file demo is not broken — but the status code is semantically misleading for a real-world
> authorization scenario.
