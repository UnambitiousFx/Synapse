# [Bug]: `Guid.Empty` accepted as an inbound correlation ID

**Severity:** Medium
**Area:** AspNetCore mapping
**Discovered on:** `main`, .NET 10, while designing cross-boundary context propagation
**Status:** ✅ **Resolved** on `main` — see [Resolution](#resolution).

> **TL;DR.** `Guid.TryParse` happily parses `"00000000-0000-0000-0000-000000000000"`, so an all-zeros
> inbound header became the request's correlation ID — colliding with the value that means "not set".

> **Superseded by the v2 context-propagation refactor.** Every mechanism named below is gone:
> `UseCorrelationId` is now `UseSynapsePropagation`, the inbound `X-Correlation-Id` read was removed in
> favour of the W3C `traceparent` header, identity is a 32-hex trace id rather than a `Guid` (so there is
> no `Guid.Empty` sentinel), `CorrelationContext` was deleted, and the in-memory outbox no longer
> partitions by correlation id — it holds one flat item collection. Read this report as history.

---

## Describe the bug

`UseCorrelationId` adopted any inbound `X-Correlation-Id` that `Guid.TryParse` accepted. All-zeros
parses successfully, but `Guid.Empty` is precisely the sentinel `CorrelationContext.CurrentCorrelationId`
uses to mean "no correlation ID has been set". Two things followed:

1. Log entries for the request reported a meaningless all-zeros correlation ID.
2. The in-memory outbox partitions by `CorrelationContext.CurrentCorrelationId`, so events stored during
   the request landed in the *same* partition as every request that had never initialized a context.

A caller could reach both effects by sending one header, and any client library defaulting an unset
correlation ID to `Guid.Empty` would trigger it accidentally.

---

## Steps to reproduce

1. Run an app with `app.UseCorrelationId()`.
2. `curl -i -H 'X-Correlation-Id: 00000000-0000-0000-0000-000000000000' http://localhost:5000/orders -X POST -d '{...}'`
3. Observe the response header and the correlation ID in the request's log scope.

---

## Expected behavior

The all-zeros value is refused and a server-generated correlation ID is used instead.

---

## Actual behavior

`X-Correlation-Id: 00000000-0000-0000-0000-000000000000` was echoed back and used throughout the
request, and outbox events were stored under the `Guid.Empty` partition.

---

## Code sample

```csharp
// src/Synapse.AspNetCore/ApplicationBuilderExtensions.cs — before
if (ctx.Request.Headers.TryGetValue("X-Correlation-Id", out var incoming)
    && Guid.TryParse(incoming, out var correlationId))   // true for all-zeros
{
    setter.Context = setter.Context.WithCorrelationId(correlationId);
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

The guard tested only parseability. `Guid.Empty` is a valid `Guid` but carries sentinel meaning in
`CorrelationContext` (documented as "or `Guid.Empty` if not set"), so parseability was the wrong test.

### Resolution

Extraction now requires the parsed value to be non-empty, in both places an inbound correlation ID can
arrive:

- the dedicated header, in `ApplicationBuilderExtensions.TryReadHeaderCorrelationId`;
- the W3C `baggage` entry, in `W3CContextPropagator.TakeGuid`.

The same pass also made the header read reject a duplicated header rather than resolving it
arbitrarily through the `StringValues`-to-`string` conversion.

**Verification (at the time).** Unit tests over the header read and the `baggage` extraction, covering
all-zeros, unparseable and empty values, plus an end-to-end check against `examples/MinimalApi` that
sending the all-zeros header produced a freshly generated Guid v7 instead.

None of those symbols or tests survive; the equivalent guards today are
`UseSynapsePropagation_WithNoInboundTraceContext_LeavesInboundStateEmpty` and
`UseSynapsePropagation_IgnoresAnInboundTraceIdHeader` in
`test/Synapse.AspNetCore.Tests/ApplicationBuilderExtensionsTests.cs`, plus the `Extract` tests in
`test/Synapse.Tests/Propagation/W3CContextPropagatorTests.cs`.
