# [Bug]: Correlation ID fabricated on non-mediator routes

**Severity:** Low
**Area:** AspNetCore mapping
**Discovered on:** `main`, .NET 10, while designing cross-boundary context propagation
**Status:** ✅ **Resolved** on `main` — see [Resolution](#resolution).

> **TL;DR.** The response-header writer read `IContextAccessor.Context`, which *creates* a context on
> first access — so every static file and health-check response got a correlation ID invented for it,
> and the `try`/`catch` meant to prevent exactly that was unreachable.

---

## Describe the bug

`UseCorrelationId` registered an `OnStarting` callback that read `accessor.Context` and wrote the
correlation ID onto the response. Its XML documentation claimed:

> When no `IContextAccessor` is registered, or when the context has not been initialized (e.g., for
> non-mediator routes), the header is silently omitted.

That was false. `ContextHandler.Context` lazily *creates* the context when read, so the callback could
never observe an uninitialized context — it manufactured one. Consequences:

1. Responses from routes that never touch the mediator carried a correlation ID that appears nowhere in
   any log, since no work was done under it.
2. Each fabricated ID also created a new key in the in-memory outbox's partition dictionary.
3. The `catch` block guarding the "not initialized" case was dead code, and the comment inside it
   described behavior that did not exist.

---

## Steps to reproduce

1. Run an app with `app.UseCorrelationId()` and any endpoint that does not use the mediator.
2. `curl -i http://localhost:5000/` (or any static file).
3. Observe an `X-Correlation-Id` header on the response.

---

## Expected behavior

No correlation ID header on a response for a request that never created a context.

---

## Actual behavior

A freshly generated Guid v7 was returned on every such response.

---

## Code sample

```csharp
// src/Synapse.AspNetCore/ApplicationBuilderExtensions.cs — before
private static Task OnStartingResponse(HttpContext httpContext)
{
    try
    {
        var accessor = httpContext.RequestServices.GetService<IContextAccessor>();
        if (accessor is not null)
        {
            // Reading .Context CREATES the context — this never throws, and never skips.
            httpContext.Response.Headers.TryAdd("X-Correlation-Id",
                accessor.Context.CorrelationId.ToString());
        }
    }
    catch
    {
        // Context not initialized for non-mediator routes — skip silently.  (unreachable)
    }

    return Task.CompletedTask;
}
```

---

## Library version

`main` (pre-release, v2 development)

## .NET version

.NET 10.0

## Operating system

macOS

---

## Additional context

### Root cause

There was no way to ask whether a context existed without creating one: `IContextAccessor` exposed only
`Context`, whose getter is what performs the lazy creation.

### Resolution

Added `bool IsInitialized` to `IContextAccessor`, implemented in `ContextHandler` as a plain null check
on the backing field. The response writer now tests `accessor is { IsInitialized: true }`, the dead
`try`/`catch` is gone, and the XML documentation describes what the code actually does.

> **Later note.** The `IsInitialized` fix described here is unchanged and still what prevents the header
> from being written on non-mediator routes. Everything around it was renamed by the v2
> context-propagation refactor: the middleware is `UseSynapsePropagation`, the response header is
> `options.TraceIdHeaderName` (default `Trace-Id`, alongside a `traceresponse` header) carrying the flow's
> 32-hex W3C trace id rather than a `Guid`, there is no `CorrelationId` member on the context, and the
> in-memory outbox no longer has a partition dictionary for a fabricated id to pollute (consequence 2
> above).

**Verification.** `UseSynapsePropagation_WhenNoContextWasCreated_WritesNoResponseHeader` in
`test/Synapse.AspNetCore.Tests/ApplicationBuilderExtensionsTests.cs` asserts both that no header is
written and that `Context` was never read. Confirmed end to end against `examples/MinimalApi`:
`curl -i http://localhost:5225/` returns zero trace-id headers.
