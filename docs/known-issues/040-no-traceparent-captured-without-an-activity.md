# [Bug]: No `traceparent` is propagated when there is no ambient activity

**Severity:** Medium
**Area:** Outbox
**Discovered on:** `main`, .NET 10, code review of the v2 trace-context rework
**Status:** ✅ **Resolved** on `main` — see [Resolution](#resolution).

> **TL;DR.** `Inject` wrote trace context only when `Activity.Current` existed, so on a host with no tracing wired —
> exactly the host `ContextIdentity` mints a trace id for — outbox entries were stored with no `traceparent` at all
> and their dispatch became a disconnected root, which is the one thing `OutboxEntry.Headers` exists to prevent.

---

## Describe the bug

```csharp
// src/Synapse/Propagation/W3CContextPropagator.cs — before
if (Activity.Current is { } activity)
{
    DistributedContextPropagator.Current.Inject(activity, carrier, TraceOnlySetter);
}
```

`OutboxManager.CaptureHeaders` delegates to `Inject`, so an entry's stored headers held whatever `Inject` wrote. With
no `ActivityListener` registered, `StartActivity` returns null and `Activity.Current` stays null — the case the
design deliberately supports, since `ContextIdentity.ForUnitOfWork` mints a trace id precisely so that such a host
can still correlate. But nothing wrote it to the carrier, so:

- `entry.Headers` contained baggage and nothing else;
- `Extract` on dispatch returned `default` for `Trace`;
- `StartActivity(…, default)` produced a new root, in a new trace, with no relation to the request that stored the
  entry.

The trace id the context had all along never left the process, so the promise in `OutboxEntry.Headers`' own
documentation — *"the only way a dispatched entry can tie the resulting work back to its cause"* — did not hold for
the hosts that most need it. Outbound HTTP had the same hole: with no activity, no `traceparent`, so the receiver
minted a fresh trace and the flow broke at the hop.

`ApplicationBuilderExtensions.TryBuildTraceResponse` already worked around a nearby symptom by reading
`context.TraceId` rather than the activity, which was the hint that the propagator was the wrong way round.

---

## Steps to reproduce

1. Build a host with no tracing (no `ActivityListener`, no OpenTelemetry).
2. Store an event through the outbox from inside a unit of work.
3. Inspect the stored `Headers`, then process the outbox and inspect the dispatch activity's trace id.

---

## Expected behavior

An entry stored with a context always carries that context's trace id, and the dispatch continues that trace. Only
an event stored outside any unit of work has nothing to continue.

---

## Actual behavior

`Headers` held no `traceparent`, and the dispatch activity was a root in an unrelated trace.

---

## Code sample

```csharp
// No ActivityListener anywhere in the process
var context = new Context(identity);           // identity.TraceId was minted
propagator.Inject(context, carrier);

// before: false — the trace id existed but never reached the carrier
Console.WriteLine(carrier.Headers.ContainsKey(PropagationKeys.TraceParent));
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

Injection was written as "serialize the ambient activity" rather than "serialize the flow's identity". The activity
is one *source* of that identity — `ContextIdentity` already treats it as such, with a minted fallback — but
`Inject` had no fallback of its own, so the identity Synapse had established was propagated only when the platform
happened to hold it too.

### Resolution

With no usable activity, `Inject` now writes the header itself from `IContext.TraceId`:

```csharp
else if (SyntheticTraceParent(context.TraceId) is { } traceParent)
{
    carrier.Set(PropagationKeys.TraceParent, traceParent);
}
```

The value is `00-<context trace id>-<derived parent id>-00`:

- **The parent id is derived from the trace id** (its first 16 hex characters) rather than random, so every hop of
  one flow names the same synthetic span instead of a different one per injection. A span id is required by the
  header format; an all-zeros one is invalid, so something has to fill it.
- **The sampled flag is `00`.** This span was never recorded, which is precisely what a non-recording peer reports,
  and it is what tells a receiver to continue the trace without expecting to find the parent.

A recording activity still wins — a real span id is one a backend can resolve. The guard also covers a
non-W3C-format activity, an activity with an all-zeros trace id (issue
[031](031-zero-ambient-trace-id-accepted-as-identity.md)), and a custom `IContextFactory` returning a trace id that
is not 32 hex characters, in which case no header is written at all: an unparseable `traceparent` is worse than an
absent one.

One consequence worth stating: a receiver of such a header sets `IContext.CausationId` to the derived span id, so a
no-tracing flow now reports a causation id that names no real span. In a process with no spans at all nothing could
resolve it anyway, and it is stable across the flow rather than arbitrary. What this fix does **not** touch is the
case where an activity exists but its trace id differs from the context's — untrusted mode, as described in issue
[032](032-untrusted-mode-still-adopts-caller-trace-id.md). There the activity still wins, so the wire continues the
trace the host's instrumentation is recording into; deciding which of the two identities such a header should carry
is a separate question from having one at all.

**Verification.** `test/Synapse.Tests/Propagation/W3CContextPropagatorTests.cs` —
`Inject_WithNoActivity_WritesATraceParentDerivedFromTheContext` (asserts the exact header and that
`ActivityContext.TryParse` accepts it with the sampled flag off),
`Inject_TwiceWithNoActivity_NamesTheSameSyntheticSpan`,
`InjectThenExtract_WithNoActivityListener_CarriesBaggageAndTheContextsTraceId`,
`Inject_WithARecordingActivity_PrefersTheRealSpanOverTheDerivedOne` and the
`Inject_WithAnUnusableContextTraceId_WritesNoTraceParent` theory.
`test/Synapse.Tests/Publish/Outbox/OutboxFlowIdentityTests.cs` —
`StoreThenDispatch_WithNoProducingActivity_StillContinuesTheContextsTrace` drives store-then-dispatch with nothing
recording and asserts the dispatch activity lands in the context's trace, while
`StoreThenDispatch_WithNoContextAtAll_StartsItsOwnTrace` pins the case that genuinely has nothing to continue. All
of the behavioural ones fail against the previous implementation.
