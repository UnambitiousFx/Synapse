# [Bug]: Untrusted propagation mode still adopts the caller's trace ID

**Severity:** High
**Area:** AspNetCore mapping
**Discovered on:** `main`, .NET 10, code review of the v2 trace-context rework
**Status:** ✅ **Resolved** on `main` — see [Resolution](#resolution).

> **TL;DR.** `TrustIncomingHeader = false` cleared the inbound `ActivityContext`, but ASP.NET Core had
> already parented the request `Activity` to the caller's `traceparent`, so `ContextIdentity.ForUnitOfWork`
> read the forged trace ID back out of `Activity.Current` — the header was honoured after all.

---

## Describe the bug

`PropagationOptions.TrustIncomingHeader` documents a security guarantee: with it off, *"the trace id is
minted server-side, inbound trace context and baggage are discarded"*. The middleware implemented that by
zeroing the trace context it stored:

```csharp
// src/Synapse.AspNetCore/ApplicationBuilderExtensions.cs — before
return new PropagatedContext(default, baggage);
```

Clearing the *stored* trace context is not the same as refusing the caller's identity. ASP.NET Core's
`HostingApplicationDiagnostics` parses the inbound `traceparent` and parents the request `Activity` to it
**before any middleware runs**. `ContextIdentity.ForUnitOfWork` falls back to the ambient activity when
nothing was propagated — which is exactly the state the middleware had just manufactured:

```csharp
// src/Synapse.Abstractions/ContextIdentity.cs — before
var traceId = inbound.Trace != default
    ? inbound.Trace.TraceId.ToHexString()
    : AmbientOrMintedTraceId(activity);       // ← the caller's trace id, via Activity.Current

var causationId = inbound.Trace != default
    ? ToNullableHex(inbound.Trace.SpanId)
    : ToNullableHex(activity?.ParentSpanId ?? default);   // ← the caller's span id
```

So on any application with request tracing enabled — OpenTelemetry, Application Insights, i.e. precisely
the deployments that care about trace correlation — a hostile client's chosen trace ID still became
`IContext.TraceId`, and its span ID became `IContext.CausationId`. The option only worked on hosts with no
tracing at all, where there was nothing to correlate in the first place.

The existing tests did not catch it because they asserted on `store.Inbound` (which was correctly zeroed)
and never on the identity the factory derived from it.

---

## Steps to reproduce

1. Configure `UseSynapsePropagation(o => o.TrustIncomingHeader = false)`.
2. Enable request tracing (register an `ActivityListener`, or add OpenTelemetry's ASP.NET Core
   instrumentation) so the request `Activity` adopts the inbound `traceparent`.
3. Send a request with `traceparent: 00-<attacker-chosen-trace-id>-<span-id>-01`.
4. Read `IContext.TraceId` in a handler, or read the `Trace-Id` response header.

---

## Expected behavior

The trace ID is minted server-side and bears no relation to the inbound header. `CausationId` is `null`,
because the only candidate predecessor span is the untrusted caller's. The caller's trace ID remains
available as baggage under `ClientTraceIdBaggageKey`.

---

## Actual behavior

`IContext.TraceId` was the attacker-chosen trace ID and `IContext.CausationId` was the attacker-chosen
span ID, identically to trusted mode.

---

## Code sample

```csharp
app.UseSynapsePropagation(o => o.TrustIncomingHeader = false);

// GET /orders  with  traceparent: 00-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa-bbbbbbbbbbbbbbbb-01
app.MapGet("/orders", (IContext context) => context.TraceId);
// before: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"  (the value the option promises to discard)
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

Two independent paths carry inbound trace context into the process — the header Synapse reads and the
ambient `Activity` the host reads — and the option only closed one of them. `PropagatedContext` could not
express the difference between *"nothing was propagated"* (where adopting the ambient activity is right and
is what makes in-process flows traceable) and *"what was propagated was rejected"* (where it is a bypass).

### Resolution

`PropagatedContext` gained a `SuppressAmbientTrace` flag, defaulting to `false` so the ordinary path is
unchanged. A boundary that refuses inbound trace context sets it, and `ForUnitOfWork` then skips the
ambient activity entirely rather than merely preferring the inbound value:

```csharp
if (inbound.SuppressAmbientTrace)
{
    // Nothing about the caller may become this flow's identity, and the ambient activity is the
    // caller's by proxy. Causation is dropped with it: the only candidate span id is the caller's.
    return new ContextIdentity(ActivityTraceId.CreateRandom().ToHexString(), null, DateTimeOffset.UtcNow);
}
```

The middleware's `Untrusted` reduction sets the flag alongside the values it drops.

Note that in this mode `IContext.TraceId` deliberately diverges from `Activity.Current.TraceId`, which
remains parented to the caller — Synapse cannot un-parent an activity the host created before the pipeline
started. Refusing the caller's identity for Synapse's own correlation is what the option promises; making
the host's tracing distrust the header is a host/exporter configuration concern.

**Verification.** `test/Synapse.Tests/Contexts/ContextHandlerTests.cs` —
`Context_WhenAmbientTraceIsSuppressed_MintsInsteadOfAdoptingTheAmbientActivity` starts an activity parented
to a caller trace context and asserts the resulting identity is neither that trace ID nor carries a
causation ID; `Context_WhenAmbientTraceIsSuppressedButTraceWasAdoptedAnyway_PrefersTheInboundTrace` pins the
precedence. `test/Synapse.AspNetCore.Tests/ApplicationBuilderExtensionsTests.cs` —
`UseSynapsePropagation_WhenNotTrustingAndTheHostAdoptedTheCallersTrace_MintsTheTraceId` drives the full
middleware path with the host activity parented to the caller, and
`UseSynapsePropagation_WhenTrusting_LeavesTheAmbientActivityUsable` asserts the default mode suppresses
nothing.
