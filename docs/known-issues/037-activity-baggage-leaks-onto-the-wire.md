# [Bug]: `Activity.Baggage` leaks onto the wire alongside the context's own

**Severity:** High
**Area:** Observability
**Discovered on:** `main`, .NET 10, code review of the v2 trace-context rework
**Status:** ✅ **Resolved** on `main` — see [Resolution](#resolution).

> **TL;DR.** `Inject` let the platform's `DistributedContextPropagator` write its own baggage header from
> `Activity.Baggage`, so inbound baggage that untrusted mode had deliberately dropped was forwarded anyway — and
> when the context carried no baggage, nothing overwrote it.

---

## Describe the bug

```csharp
// src/Synapse/Propagation/W3CContextPropagator.cs — before
// Trace context first: the platform may also write a baggage header from Activity.Baggage, which the
// context-owned baggage below deliberately replaces.
if (Activity.Current is { } activity)
{
    DistributedContextPropagator.Current.Inject(activity, carrier, Setter);
}

var header = BaggageCodec.Format(context.Baggage);
if (header is not null)
{
    carrier.Set(PropagationKeys.Baggage, header);
}
```

The comment describes a replacement that does not reliably happen:

1. **The header names need not match.** `DistributedContextPropagator.Current` writes baggage under
   `Correlation-Context` on .NET 8's default propagator and under `baggage` on the W3C one. Where it uses the
   legacy name, Synapse's write lands on a *different* key and both headers travel — two baggage headers with
   different contents on one request, and `Extract` reads the legacy one whenever `baggage` is absent.
2. **`Format` returns `null` for empty baggage.** With no context baggage there is no `Set` call at all, so
   whatever the platform wrote stands unopposed.

Either way the content is caller-controlled: ASP.NET Core populates `Activity.Baggage` from the inbound
`baggage` header before any middleware runs. So a service running with
`PropagationOptions.TrustIncomingHeader = false` — which drops inbound baggage from the context precisely so it is
not forwarded or logged — forwarded it regardless, on every outbound call made under the request's activity.

---

## Steps to reproduce

1. Run with request tracing enabled and `TrustIncomingHeader = false`.
2. Send a request carrying `baggage: tenant=victim`.
3. From a handler, make an outbound call through `SynapsePropagationHandler` (or call `Inject` directly).
4. Inspect the outgoing headers.

---

## Expected behavior

Exactly one baggage header leaves the process, holding exactly `IContext.Baggage`. A context with no baggage
produces no baggage header at all.

---

## Actual behavior

The caller's baggage was forwarded — under `Correlation-Context`, or under `baggage` when the context had nothing
of its own to overwrite it with.

---

## Code sample

```csharp
using var activity = source.StartActivity("outbound");
activity!.AddBaggage("caller.supplied", "spoofed");   // as ASP.NET Core does from the inbound header

var context = new Context(identity);                  // untrusted mode dropped the inbound baggage
propagator.Inject(context, carrier);

// before: carrier holds a baggage (or Correlation-Context) header containing caller.supplied=spoofed
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

Two components were writing the same logical header, and the code assumed the later write always overwrote the
earlier one. That holds only when both use the same key and the later write always happens; neither is guaranteed.

### Resolution

The setter handed to the platform propagator now drops baggage keys, so the platform writes trace headers only:

```csharp
private static readonly DistributedContextPropagator.PropagatorSetterCallback TraceOnlySetter =
    static (carrier, key, value) =>
    {
        if (string.Equals(key, PropagationKeys.Baggage, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(key, PropagationKeys.LegacyBaggage, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        …
    };
```

With the platform's copy suppressed, "the context is the only source of outbound baggage" stops depending on
overwrite order: Synapse writes the header when the context has entries, and no baggage header exists when it does
not. The class docs now state that division of labour instead of describing a replacement.

The one thing this does not do is remove a baggage header that something else put on the carrier before
`Inject` ran — `IPropagationCarrier` has no remove operation. Synapse never *adds* caller baggage, which is the
security-relevant half.

**Verification.** `test/Synapse.Tests/Propagation/W3CContextPropagatorTests.cs` —
`Inject_WithEmptyContextBaggageAndBaggageOnTheActivity_WritesNoBaggageHeader` (fails against the previous code on
every runtime) and `Inject_WithBaggageOnTheAmbientActivity_DoesNotForwardIt`, which pins the invariant under both
header names; it is the discriminating test on runtimes whose default propagator uses `Correlation-Context`.
