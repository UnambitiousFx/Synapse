# [Bug]: Scoped `IContext` snapshot diverges from the accessor after `WithCorrelationId`

**Severity:** Medium
**Area:** Core DI
**Discovered on:** `main`, .NET 10, while designing cross-boundary context propagation
**Status:** ✅ **Resolved** on `main` — see [Resolution](#resolution).

> **TL;DR.** `IContext` was registered as a scoped snapshot of the accessor's context, and
> `WithCorrelationId` returned a **new** boxed instance — so anything that resolved `IContext` before the
> correlation-ID middleware ran kept the old identity while the response header reported the new one.

> **Naming note.** This was fixed as part of the v2 context-propagation refactor, which also renamed the
> vocabulary the report uses: `UseCorrelationId` is now `UseSynapsePropagation`, inbound identity arrives
> via the W3C `traceparent` header (not `X-Correlation-Id`), and the context's identity is `TraceId` — a
> 32-hex trace id — rather than a `Guid` `CorrelationId`. Reproducing the original symptom today means
> reading `TraceId` and sending `traceparent`.

---

## Describe the bug

Two registrations combined badly with a mutable, struct-based context:

```csharp
services.TryAddScoped<IContextAccessor>(sp => sp.GetRequiredService<ContextHandler>());
services.AddScoped<IContext>(sp => sp.GetRequiredService<IContextAccessor>().Context);
```

(Both registrations are still there, unchanged — what the fix removed is the *mutation*, not the
snapshotting. See [Resolution](#resolution).)

`IContext` is resolved once per scope and then **cached by the container**. `Context` was a
`readonly record struct` implementing `IContext`, so every assignment to an `IContext`-typed location
boxed it, and `WithCorrelationId` produced a *second, distinct* box via a `with` expression.

When the correlation-ID middleware did `setter.Context = setter.Context.WithCorrelationId(id)`, it
replaced the accessor's field but could not touch the box the container had already cached. Any
component that had resolved `IContext` earlier in the scope kept the pre-middleware correlation ID.
Handlers and `LoggingEnrichmentBehavior` would log one correlation ID while the response header — read
through `IContextAccessor` — reported another.

The reason this did not surface in practice was ordering luck: handlers and behaviors are scoped, so
they resolve during endpoint execution, after the middleware. Any component resolving `IContext` earlier
— an endpoint filter, a custom middleware, an eagerly constructed scoped service — would have split the
identity in two.

A related waste: `setter.Context` in the middleware *materialized* a full factory context (a fresh Guid
v7 plus a metadata dictionary) purely so the `with` copy could discard it.

---

## Steps to reproduce

1. Register a middleware **before** `app.UseCorrelationId()` that resolves `IContext` and records its
   `CorrelationId`.
2. Send a request with a valid `X-Correlation-Id` header.
3. Compare the recorded value, the correlation ID in the handler's log scope, and the response header.

---

## Expected behavior

One context identity per scope: every reader and the response header agree.

---

## Actual behavior

The pre-middleware reader and the handler reported the factory-generated correlation ID; the response
header reported the inbound one.

---

## Code sample

```csharp
// src/Synapse.AspNetCore/ApplicationBuilderExtensions.cs — before
var setter = ctx.RequestServices.GetService<IContextSetter>();
if (setter is not null)
{
    // Creates a context if none exists, then replaces it with a different box.
    // The container's cached IContext still points at the original.
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

Identity was mutated *after* the context existed, on a type whose interface representation is a boxed
copy, behind a DI registration that caches the first box it sees. Any one of those three alone is fine;
together they let two views of "the current context" disagree.

### Resolution

Identity is now decided when the context is created, and cannot be changed afterwards:

- `IContextFactory.Create` takes a `PropagatedContext` describing the inbound state.
- Boundary adapters write that state into a new scoped `IInboundContextStore` instead of mutating a
  context. The factory consumes it on first access, so an adapter only has to run before the first
  component *reads* the context — ordering against other components no longer matters.
- `IContext.WithCorrelationId` and the `IContextSetter` interface were removed;
  `IContextAccessor.Context` is get-only.
- `Context` became a `sealed class`. It was boxed at every use anyway, so the struct bought nothing
  while making two-boxes-disagree possible.

This also removes the discarded double construction: the middleware no longer touches the context at
all.

**Verification.** `test/Synapse.Tests/Contexts/ContextHandlerTests.cs` —
`ResolvedIContext_AndAccessorContext_AreTheSameInstance` asserts reference equality between the injected
`IContext` and the accessor's context in a real DI scope, and
`ResolvedIContext_WhenInboundStoreIsPopulatedFirst_CarriesTheInboundTraceId` asserts the inbound
value survives to the injected context. Confirmed end to end against `examples/MinimalApi`: an inbound
`traceparent` reaches the notification handler at the far end of the order → event chain and matches the
`Trace-Id` response header.
