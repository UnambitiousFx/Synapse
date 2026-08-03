# [Bug]: CQRS boundary marker leaks when a handler throws (no `finally`)

**Severity:** Medium
**Area:** Pipeline / CQRS enforcement
**Discovered on:** `feature/typed-pipeline-behaviors`, .NET 10
**Status:** ✅ **Resolved** — fixed on `feature/typed-pipeline-behaviors`.

> **Note:** the helper was named `CqrsBoundaryMetadata` when this was written and is now
> `CqrsBoundaryMarker`; the text below uses the current name. The marker itself is no longer a string
> key in an `IContext` metadata bag — issue
> [028](028-cqrs-boundary-markers-leak-into-every-log-entry.md) moved it to an internal
> `CqrsBoundaryFeature` context feature. The `try`/`catch` structure this fix introduced is unchanged.

---

## Describe the bug

`CqrsBoundaryEnforcementBehavior<TRequest>` and `CqrsBoundaryEnforcementBehavior<TRequest, TResponse>`
call `CqrsBoundaryMarker.Add` before invoking the rest of the pipeline and
`CqrsBoundaryMarker.Remove` **after** `await next(...)`. The `Remove` call is not wrapped in a
`finally`, so when `next(...)` throws (any handler or inner-behavior exception, not just a boundary
violation), the boundary marker is never removed from the `IContext`.

If the same scoped `IContext` is subsequently used to dispatch another request (e.g. a compensation
or retry send within the same scope after catching the exception), `CqrsBoundaryMarker.Validate`
sees the stale marker and throws a **spurious** `CqrsBoundaryViolationException`.

---

## Steps to reproduce

1. Dispatch a command whose handler throws a normal (non-boundary) exception.
2. Catch it at the call site and, within the same DI scope / `IContext`, dispatch another request.

---

## Expected behavior

The boundary marker is cleared regardless of whether the inner pipeline succeeded or threw; a later
send in the same scope is evaluated against a clean boundary state.

---

## Actual behavior

The marker remains set after the exception. The next send in the same context throws
`CqrsBoundaryViolationException` ("Cannot send request '…' within a request handler") even though no
nesting is actually occurring.

---

## Root cause

`src/Synapse/Pipelines/CqrsBoundaryEnforcementBehavior.cs` (≈ lines 44 and 88):

```csharp
CqrsBoundaryMarker.Add(_context, requestName);
var response = await next(request, cancellationToken);
CqrsBoundaryMarker.Remove(_context); // skipped if next() throws
return response;
```

---

## Resolution

Both behavior variants now wrap `next(...)` in `try { ... } catch { CqrsBoundaryMarker.RemoveIfPresent(_context); throw; }`.
On the exception path the marker is cleared via a new tolerant `RemoveIfPresent` helper (no throw) and the
original exception is rethrown unmasked. The success path keeps the strict `Remove` so handler tampering
with the marker is still surfaced (see the `Should_throw_when_user_manually_removes_boundary_enforcement_key_from_context`
tests). A blanket `try/finally` with a tolerant `Remove` was intentionally **not** used because it would have
suppressed that tamper check on the success path.

Covered by `Should_clear_boundary_marker_after_handler_throws_so_next_send_in_scope_succeeds` and its
with-response variant in `test/Synapse.Tests/CqrsBoundaryEnforcementTests.cs`.

## Library version

`feature/typed-pipeline-behaviors` (pre-release)

## .NET version

.NET 10.0
