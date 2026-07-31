# [Bug]: Tracing capture stores zero/invalid trace IDs and stringifies twice

**Severity:** Medium
**Area:** Observability / `Context`
**Discovered on:** `feature/typed-pipeline-behaviors`, .NET 10
**Status:** ✅ **Resolved** — see the supersession note below.

> **Superseded by the v2 context-propagation refactor.** `Context.CaptureTracingContext`, the
> `Tracing.TraceId` / `Tracing.ParentSpanId` metadata keys and the `IContext` metadata bag they wrote to
> no longer exist. Trace state is no longer snapshotted into the context at all: it stays ambient on
> `Activity.Current`, and the context carries a typed `ContextIdentity` (`TraceId`, `CausationId`,
> `OccurredAt`) computed once via `ContextIdentity.ForUnitOfWork` — one `ToHexString()` per id, and span
> ids guarded against `default`.
>
> The all-zeros trace id described here outlived the rewrite in one spot, the ambient-`Activity` fallback
> in `ForUnitOfWork`, and was fixed as issue
> [031](031-zero-ambient-trace-id-accepted-as-identity.md).

---

## Describe the bug

`Context.CaptureTracingContext` guards storing trace metadata with
`!string.IsNullOrEmpty(activity.TraceId.ToString())`. A default/zero `ActivityTraceId` stringifies
to the 32-character all-zeros string, which is **non-empty**, so the guard never short-circuits.
When `Activity.Current` exists with a zero `TraceId` or `ParentSpanId` (e.g. an Activity created
without a parent, or before any listener is recording), invalid all-zero IDs are written into the
context metadata and pollute downstream telemetry.

Separately, the guard calls `.ToString()` **twice** per id (once for the check, once to store) on a
per-context-creation path.

---

## Steps to reproduce

1. Create or run within an `Activity` whose `TraceId` is default/zero (no listener recording, or no
   parent context).
2. Create a `Context` and read its `Tracing.TraceId` / `Tracing.ParentSpanId` metadata.

---

## Expected behavior

No trace metadata is stored for a zero/invalid trace id; valid ids are stored exactly once.

---

## Actual behavior

`Tracing.TraceId` / `Tracing.ParentSpanId` are populated with `"00000000000000000000000000000000"`
(and zero span id), which downstream correlation treats as a real-but-invalid trace.

---

## Root cause

`src/Synapse/Contexts/Context.cs:159` (and the span/parent equivalents at `:161`, `:164-171`):

```csharp
if (!string.IsNullOrEmpty(activity.TraceId.ToString())) // never empty for a default ActivityTraceId
{
    SetMetadata(TracingTraceIdKey, activity.TraceId.ToString()); // second ToString()
}
```

---

## To address

- Guard on the actual default value, e.g. `activity.TraceId != default` (and likewise for span /
  parent span ids), rather than on an empty string.
- Capture each `ToString()` into a local and reuse it.
- Consider gating the whole capture behind a config flag or computing it lazily, since it runs on
  every context creation (see also the efficiency note for this method).

## Library version

`feature/typed-pipeline-behaviors` (pre-release)

## .NET version

.NET 10.0
