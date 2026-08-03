# [Bug]: `SynapsePropagationHandler` never sees the request's context

**Severity:** High
**Area:** Observability
**Discovered on:** `main`, .NET 10, code review of the v2 trace-context rework
**Status:** ✅ **Resolved** on `main` — see [Resolution](#resolution).

> **TL;DR.** The handler injected a **scoped** `IContextAccessor`, but `IHttpClientFactory` builds and
> caches message handlers in a scope of its own, so `IsInitialized` was always `false` and the documented
> outbound-propagation pattern stamped nothing on any request.

---

## Describe the bug

`SynapsePropagationHandler` is registered transient and documented — in its own XML docs and in
`docs/docs/propagation.mdx` — for use with a named client:

```csharp
services.AddHttpClient("billing")
        .AddHttpMessageHandler<SynapsePropagationHandler>();
```

`IHttpClientFactory` does not construct message handlers per request. `DefaultHttpClientFactory`
creates a **separate scope per handler chain** (`CreateHandlerEntry` → `_scopeFactory.CreateScope()`) and
caches that chain for the handler lifetime (2 minutes by default), rotating it independently of any
request. The scoped `IContextAccessor` the handler received therefore belongs to the factory's scope and is
never the accessor of the unit of work making the call:

```csharp
// src/Synapse/Propagation/SynapsePropagationHandler.cs — before
if (_contextAccessor.IsInitialized)                     // ← always false in the factory's scope
{
    _propagator.Inject(_contextAccessor.Context, new HttpRequestMessagePropagationCarrier(request));
}
```

Because the guard exists precisely so that an outbound call outside a unit of work does not invent a flow,
the failure is silent: no exception, no log, just no `baggage` header, forever.

The mirror-image hazard is worse. If anything ever materialized a context in the factory's scope, that one
context would be reused for **every** request the cached handler served until it rotated, cross-attributing
unrelated calls to a single flow.

The existing tests all constructed the handler directly with a stub accessor, so they exercised only the
arrangement that production never produces.

---

## Steps to reproduce

1. `AddSynapse(...)`, then `services.AddHttpClient("billing").AddHttpMessageHandler<SynapsePropagationHandler>();`
2. In a request handler, set baggage on `IContext` and call the `billing` client.
3. Inspect the outgoing request headers.

---

## Expected behavior

The outgoing request carries the current unit of work's context baggage.

---

## Actual behavior

No `baggage` header was written. (`traceparent` still appeared, because `SocketsHttpHandler` injects it
itself — which made the omission easy to miss.)

---

## Code sample

```csharp
app.MapPost("/orders", async (IContext context, IHttpClientFactory factory) =>
{
    context.SetBaggage("tenant.id", "contoso");

    var client = factory.CreateClient("billing");
    await client.PostAsync("/charges", content);
    // before: request has traceparent (from the platform) but no baggage header at all
});
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

Treating an `IHttpClientFactory`-built `DelegatingHandler` as a scope-resident service. Its lifetime is
owned by the factory's cache, not by the request, so constructor injection cannot reach request state — the
same reason ASP.NET Core's own `HeaderPropagationMessageHandler` reads an `AsyncLocal`-backed store rather
than injecting the request's services, and the reason `IHttpContextAccessor` exists at all.

### Resolution

The context is now mirrored onto the execution context by its owner and read from there by the handler.

`AmbientContext` (internal to `UnambitiousFx.Synapse`) holds an `AsyncLocal<IContext?>` with an `Exchange`
operation that returns the displaced value. `ContextHandler` publishes when it creates the context and, now
being `IDisposable`, restores the previous value when its scope is disposed — so a nested unit of work does
not outlive its scope in the ambient slot. `SynapsePropagationHandler` drops the accessor from its
constructor and reads `AmbientContext.Value`, which also makes it free of scoped dependencies, as a
transient resolved outside any scope must be.

The mirror is deliberately narrow: anything resolved from the scope doing the work still injects
`IContextAccessor` or `IContext`. An ambient read there would work by accident and break as soon as the
value were read from a sibling execution branch.

**Verification.** `test/Synapse.Tests/Propagation/SynapsePropagationHandlerTests.cs` —
`SendAsync_WhenBuiltOutsideTheScopeDoingTheWork_StillStampsBaggage` resolves the handler from one DI scope
and materializes the context in another, reproducing the factory's arrangement, and
`Handler_ResolvesFromTheRootProvider_WithScopeValidationEnabled` pins the absence of a captive dependency.
`test/Synapse.Tests/Contexts/ContextHandlerTests.cs` covers publishing on first read, restoring on dispose,
and leaving the slot untouched when a scope never read its context.
