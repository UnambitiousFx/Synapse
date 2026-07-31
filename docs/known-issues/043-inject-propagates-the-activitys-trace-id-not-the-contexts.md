# [Bug]: Inject propagates the activity's trace id, not the context's

**Severity:** Medium
**Area:** Observability
**Discovered on:** `feat/context-propagation`, .NET 10, code review of the v2 context-propagation rework
**Status:** ✅ **Resolved** on `feat/context-propagation` — see [Resolution](#resolution).

> **TL;DR.** `W3CContextPropagator.Inject` delegated `traceparent` to the platform propagator whenever a W3C
> activity existed, which writes the *activity's* trace id. In untrusted mode the activity is parented to the
> caller while `IContext.TraceId` is server-minted, so the caller's forged trace id was propagated onward on every
> outbound call and captured into every outbox entry — the exact value `TrustIncomingHeader = false` exists to
> refuse.

---

## Describe the bug

```csharp
// src/Synapse/Propagation/W3CContextPropagator.cs — before
if (Activity.Current is { IdFormat: ActivityIdFormat.W3C } activity &&
    activity.TraceId != default)
{
    DistributedContextPropagator.Current.Inject(activity, carrier, TraceOnlySetter);
}
else if (SyntheticTraceParent(context.TraceId) is { } traceParent)
{
    carrier.Set(PropagationKeys.TraceParent, traceParent);
}
```

The `context` parameter's trace id was used only in the fallback branch. Whenever an activity existed, the header
came from the activity and the context was ignored.

Normally the two are the same value — `ContextIdentity.ForUnitOfWork` reads the ambient activity's trace id, so
`IContext.TraceId == Activity.Current.TraceId.ToHexString()`. **Untrusted mode makes them differ on purpose.**
ASP.NET Core's own request instrumentation parses `traceparent` and parents the request activity to the caller
*before any middleware runs*, so with `TrustIncomingHeader = false` the middleware sets
`PropagatedContext.SuppressAmbientTrace` and the context is given a server-minted trace id instead (known issue
032). The activity keeps the caller's; Synapse cannot un-parent it.

So in exactly the mode built to distrust the caller's trace id, every outbound propagation carried it:

- `SynapsePropagationHandler` stamped it onto outgoing `HttpClient` requests, telling the next service that the
  caller-chosen trace id is this flow's identity;
- `OutboxManager.CaptureHeaders` stored it on the entry, so the later dispatch re-parented server-side work into
  the forged trace — the cross-flow collision the mode exists to prevent, arriving by a different route.

It also contradicted the same assembly's own reasoning. `ApplicationBuilderExtensions.TryBuildTraceResponse`
deliberately builds `traceresponse` from `context.TraceId` rather than `Activity.Id`, commented "deriving from the
context keeps the two response headers in agreement" — while `Inject` did the opposite for the request headers.

A smaller gap sat alongside it: `SyntheticTraceParent`'s guard checked length 32 and not-all-zeros, but never that
the characters were hex. A custom `IContextFactory` returning a 32-character non-hex trace id produced
`00-<non-hex>-<non-hex>-00`, which a receiver's `ActivityContext.TryParse` rejects — defeating the guard's stated
intent that "an unparseable traceparent is worse than an absent one".

---

## Steps to reproduce

1. `app.UseSynapsePropagation(o => o.TrustIncomingHeader = false);`
2. Send a request with `traceparent: 00-11111111111111111111111111111111-2222222222222222-01`.
3. From a handler, call an `HttpClient` with `SynapsePropagationHandler` attached, and store an event with
   `EmitMode.Outbox`.
4. Inspect the outgoing request's `traceparent` and the stored `OutboxEntry.Headers`.

---

## Expected behavior

Both carry the server-minted trace id — the one `IContext.TraceId`, the `Trace-Id` response header and the log
scope report.

---

## Actual behavior

Both carry `11111111111111111111111111111111`, the client-supplied one.

---

## Code sample

```csharp
// Untrusted edge: the context and the ambient activity deliberately disagree
context.TraceId;                          // server-minted, e.g. 90e2f3a1…
Activity.Current!.TraceId.ToHexString();  // the caller's, 1111…

propagator.Inject(context, carrier);
carrier.Headers["traceparent"];           // "00-11111111111111111111111111111111-…" ← the caller's
```

---

## Library version

`feat/context-propagation`

## .NET version

.NET 10.0

## Operating system

macOS

---

## Additional context

### Root cause

"Delegate trace context to the platform" was applied one level too broadly. Delegating the *format* is right —
`DistributedContextPropagator` owns the header shape, and Synapse should not hand-roll it. Delegating the
*identity* is not: the platform propagator can only report what the activity says, and the activity is not the
authority on which flow this is when a boundary has refused the caller's trace context.

### Resolution

`Inject` now compares the two and lets the context win when they disagree:

```csharp
if (Activity.Current is { IdFormat: ActivityIdFormat.W3C } activity &&
    activity.TraceId != default)
{
    if (string.Equals(activity.TraceId.ToHexString(), context.TraceId, StringComparison.Ordinal))
    {
        DistributedContextPropagator.Current.Inject(activity, carrier, TraceOnlySetter);
    }
    else if (RebasedTraceParent(context.TraceId, activity) is { } rebased)
    {
        carrier.Set(PropagationKeys.TraceParent, rebased);
    }
}
```

`RebasedTraceParent` names the context's trace and the activity's span: the span id and sampled flag still
describe the span actually making the call, and only the trace it is filed under is corrected. `tracestate` is
dropped rather than forwarded — it is vendor state scoped to the caller's trace, and the header being written
names a different one. When the context's trace id is unusable, nothing is written at all; falling back to the
activity would readmit the refused value.

Every other case is unchanged: whenever the two agree — which is all trusted traffic and every flow that starts
in-process — the platform propagator writes the header exactly as before.

The trace-id guard was also extracted to `IsUsableTraceId` and now requires 32 **lowercase hex** characters, so a
custom `IContextFactory` can no longer produce an unparseable header.

### Resolution notes for readers upgrading

In untrusted mode the server-minted trace id is now what leaves the process. A downstream service therefore joins
the trace under the id *this* service logs, not the one its client picked. Spans exported by the host's own
instrumentation still carry the caller's trace id unless the tracing exporter is configured to distrust the
header as well; that asymmetry is inherent and documented in `docs/docs/aspnetcore.mdx`.

**Verification.** Four tests added to `W3CContextPropagatorTests`:
`Inject_WhenTheContextAndTheActivityDisagree_PropagatesTheContextsTraceId` (context's trace, activity's span, the
activity's trace id absent from the header),
`…_DropsTheActivitysTraceState`,
`Inject_WhenTheContextsTraceIdIsUnusableAndAnActivityExists_WritesNoTraceParent`, and two new
`InlineData` rows on `Inject_WithAnUnusableContextTraceId_WritesNoTraceParent` covering uppercase hex and
non-hex 32-character ids. Three existing tests that built a context with a random trace id under a live activity
— a state the context factory cannot produce — were corrected to build it on the activity, via a new
`NewContextOn` helper. `dotnet build -c Release` clean; full suite 687 passed / 0 failed.
