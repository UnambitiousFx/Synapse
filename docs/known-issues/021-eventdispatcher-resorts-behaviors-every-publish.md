# [Bug]: `EventDispatcher` re-sorts behaviors on every publish (per-publish allocation)

**Severity:** Low
**Area:** Pipeline / Performance
**Discovered on:** `feature/typed-pipeline-behaviors`, .NET 10
**Status:** ✅ **Resolved** — `EventDispatcher` now caches the resolved+sorted behavior array per
event type (`ConcurrentDictionary<Type, object>` keyed by `TEvent`) for its scoped lifetime, so the
`OrderBy`/`ToArray` runs once per type per scope instead of on every dispatch. The cache is
lifetime-safe because the dispatcher and event behaviors share the same (scoped) lifetime.

---

## Describe the bug

`EventDispatcher` orders its event pipeline behaviors with `OrderBy(PipelineBehaviorOrdering.OrderOf)`
on **every** `PublishAsync` call. `OrderBy` allocates an internal buffer plus the materialized
sequence each time. The request and stream pipelines, by contrast, sort once in the proxy constructor
and reuse the result.

This is functionally correct but adds avoidable allocation and CPU on the hot event-dispatch path,
contrary to the library's stated low-allocation goal, and is inconsistent with how the request/stream
pipelines handle ordering.

---

## Steps to reproduce

1. Publish events at high throughput with one or more registered event pipeline behaviors.
2. Profile allocations on the publish path.

---

## Expected behavior

Behaviors are ordered once (at construction) and the ordered list is reused across publishes, matching
the request/stream pipeline approach.

---

## Actual behavior

A fresh ordered buffer/array is allocated for the behavior list on each `PublishAsync`, increasing GC
pressure under load.

---

## Root cause

`src/Synapse/Publish/EventDispatcher.cs` (≈ line 72):

```csharp
... .OrderBy(Pipelines.PipelineBehaviorOrdering.OrderOf) ...
```

evaluated per call rather than once.

---

## To address

Sort the behavior collection once at dispatcher construction (or lazily cache it) and reuse the
ordered array on each publish.

## Library version

`feature/typed-pipeline-behaviors` (pre-release)

## .NET version

.NET 10.0
