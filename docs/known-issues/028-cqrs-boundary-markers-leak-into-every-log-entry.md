# [Bug]: CQRS boundary markers leak into every log entry

**Severity:** Medium
**Area:** Observability
**Discovered on:** `main`, .NET 10, while designing cross-boundary context propagation
**Status:** ✅ **Resolved** on `main` — see [Resolution](#resolution).

> **TL;DR.** `LoggingEnrichmentBehavior` copied *every* context metadata entry into the log scope, and
> CQRS boundary enforcement stored its internal markers in that same metadata bag — so
> `__CQRSBoundaryEnforcement` appeared in every log entry of every enforced request.

---

## Describe the bug

`LoggingEnrichmentBehavior.CreateState` enumerated `IContext.Metadata` wholesale and added each entry to
the `ILogger` scope as `Metadata_<key>`. `CqrsBoundaryMetadata` used the same untyped bag for its two
internal markers, `__CQRSBoundaryEnforcement` and `__CQRSBoundaryEnforcement_Name`.

The result: with CQRS boundary enforcement enabled, every log entry emitted during a request carried
`Metadata___CQRSBoundaryEnforcement: true` and `Metadata___CQRSBoundaryEnforcement_Name: <RequestName>`.
This is framework bookkeeping with no diagnostic value, and it inflated every structured log record and
its storage cost.

The underlying problem was that a single `string → object` metadata bag mixed values intended for
observation with values that were purely internal, and nothing in its shape distinguished them.

---

## Steps to reproduce

1. Register `LoggingEnrichmentBehavior<TRequest, TResponse>` and enable CQRS boundary enforcement for
   the same request.
2. Send the request with a structured logging provider (JSON console is enough).
3. Inspect any log entry emitted from inside the handler.

---

## Expected behavior

The log scope contains correlation information and values the application explicitly chose to surface —
not the framework's internal enforcement markers.

---

## Actual behavior

Every log entry included `Metadata___CQRSBoundaryEnforcement` and
`Metadata___CQRSBoundaryEnforcement_Name`.

---

## Code sample

```csharp
// src/Synapse/Pipelines/LoggingEnrichmentBehavior.cs — before
foreach (var metadata in context.Metadata)
{
    state[$"Metadata_{metadata.Key}"] = metadata.Value;   // includes __CQRSBoundaryEnforcement
}

// src/Synapse/Pipelines/CqrsBoundaryEnforcementBehavior.cs — before
context.SetMetadata("__CQRSBoundaryEnforcement", true);
context.SetMetadata("__CQRSBoundaryEnforcement_Name", requestName);
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

One untyped bag served two incompatible purposes — process-local framework state and
application-observable values — so a behavior that surfaced "all metadata" could not avoid surfacing
the framework's markers too.

### Resolution

The untyped metadata bag was removed and replaced by three surfaces with fixed semantics:

- typed identity properties — `TraceId`, `CausationId`, `OccurredAt` (the `ContextIdentity` record);
- `string → string` baggage, which is what crosses process boundaries;
- `IContextFeature`, for typed process-local state that is never serialized.

CQRS boundary enforcement now uses a `CqrsBoundaryFeature` rather than string metadata keys, preserving
its semantics exactly (single marker plus the crossing request's name, throw-on-missing on removal).
`LoggingEnrichmentBehavior` enriches from the typed identity plus baggage, and deliberately never from
features.

A side benefit: the marker is no longer removable by user code through a guessable string key, because
`CqrsBoundaryFeature` is internal to `UnambitiousFx.Synapse`.

**Verification.** `HandleAsync_WithContextFeatures_DoesNotLeakThemIntoScope` in
`test/Synapse.Tests/Pipelines/LoggingEnrichmentBehaviorTests.cs` asserts no scope key contains `CQRS`
or starts with `Metadata_`. The existing CQRS enforcement suite
(`test/Synapse.Tests/CqrsBoundaryEnforcementTests.cs`, including the throw-on-missing path) continues
to pass against the feature-based marker.
