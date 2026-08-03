# [Bug]: All-zeros ambient trace ID accepted as the context identity

**Severity:** Low
**Area:** Observability
**Discovered on:** `main`, .NET 10, while auditing `docs/known-issues/` against the v2 refactor
**Status:** ✅ **Resolved** on `main` — see [Resolution](#resolution).

> **TL;DR.** `ContextIdentity.ForUnitOfWork` read the ambient `Activity.Current.TraceId` without
> checking whether it was set; a default `ActivityTraceId` hex-formats to 32 zeros, which is non-empty,
> so the minting fallback never fired and the context carried `00000000000000000000000000000000`.

---

## Describe the bug

```csharp
// src/Synapse.Abstractions/ContextIdentity.cs — before
var traceId = inbound.Trace != default
    ? inbound.Trace.TraceId.ToHexString()
    : activity?.TraceId.ToHexString() ?? ActivityTraceId.CreateRandom().ToHexString();
```

The `??` only covers `Activity.Current` being **null**. An `Activity` that exists but was never started
— including one assigned directly to `Activity.Current` — has a default `TraceId`, and
`default(ActivityTraceId).ToHexString()` is a 32-character all-zeros string. It is a perfectly valid
`string`, so it was adopted as the identity, in direct contradiction of the record's own contract for
`TraceId`: *"Never null or empty."*

Downstream, that value is logged by `LoggingEnrichmentBehavior`, written to the response `Trace-Id`
header, and attached to outbox entries as flow identity — so every request in that state correlates to
the same meaningless id instead of to a distinct flow.

The sibling code path already got this right: `ToNullableHex` compares the span id against `default`
before formatting it.

This is the last surviving fragment of issue
[011](011-tracing-capture-stores-zero-trace-ids.md) — the same "all-zeros stringifies non-empty"
mistake, in the code that replaced the one 011 described.

---

## Steps to reproduce

1. Set `Activity.Current` to an unstarted `Activity` (or run under a host that does).
2. Create a context with nothing propagated inbound.
3. Read `IContext.TraceId`.

---

## Expected behavior

An unset ambient trace id is treated as no id at all, and a fresh one is minted — the same as when
`Activity.Current` is null.

---

## Actual behavior

`TraceId` was `00000000000000000000000000000000`.

---

## Code sample

```csharp
Activity.Current = new Activity("unstarted"); // never .Start()ed → default TraceId

var identity = ContextIdentity.ForUnitOfWork(PropagatedContext.None);

// before: "00000000000000000000000000000000"
Console.WriteLine(identity.TraceId);
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

Null-checking the *activity* rather than validating the *value* it carries. `ActivityTraceId` has no
"empty" string representation to trip an `IsNullOrEmpty` guard, so the only reliable check is against
`default`.

### Resolution

The ambient id is now taken only when it is actually set, falling through to `ActivityTraceId.CreateRandom()`
otherwise, mirroring how `ToNullableHex` treats span ids:

```csharp
private static string AmbientOrMintedTraceId(Activity? activity)
{
    var ambient = activity?.TraceId ?? default;

    return ambient != default
        ? ambient.ToHexString()
        : ActivityTraceId.CreateRandom().ToHexString();
}
```

The XML docs on `ForUnitOfWork` now state that an all-zeros ambient trace id is treated as absent.

**Verification.** `test/Synapse.Tests/Contexts/ContextTests.cs` —
`ContextIdentity_ForUnitOfWork_WithZeroTraceIdOnAmbientActivity_MintsATraceId` assigns an unstarted
`Activity` to `Activity.Current` and asserts the resulting `TraceId` is a 32-hex string that is not the
all-zeros id.
