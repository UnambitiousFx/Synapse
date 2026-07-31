# [Bug]: Restored outbox flow never reaches the handlers' context

**Severity:** High
**Area:** Outbox
**Discovered on:** `feat/context-propagation`, .NET 10, code review of the v2 context-propagation rework
**Status:** ✅ **Resolved** on `feat/context-propagation` — see [Resolution](#resolution).

> **TL;DR.** `DispatchEventAsync` extracted the entry's stored flow state and then used it for one thing only —
> parenting the dispatch `Activity`. The restored baggage was discarded, and the handlers kept resolving `IContext`
> from the *committing* scope, so an entry was dispatched under whichever request happened to call `CommitAsync`.
> Each entry is now dispatched in its own scope, with the restored state written to that scope's
> `IInboundContextStore`.

---

## Describe the bug

```csharp
// src/Synapse/Publish/Outbox/OutboxManager.cs — before
var restored = _propagator.Extract(new DictionaryPropagationCarrier(ReadHeaders(entry)));

using var activity = SynapseActivitySource.Source.StartActivity(
    "synapse.outbox.dispatch",
    ActivityKind.Consumer,
    restored.Trace);          // ← the only use of `restored`

…
var result = await dispatcher(@event, _eventDispatcher, cancellationToken);
//                                    ↑ the *caller's* scoped dispatcher
```

`OutboxEntry.Headers` exists so a dispatched entry can be tied back to the action that stored it. Two halves of
that were missing:

**`restored.Baggage` was never used at all.** Nothing read it, so the business values the entry was stored with —
`tenant.id`, and anything else the producing request put in baggage — were captured, persisted, extracted, and
then dropped on the floor.

**The handlers' context came from the caller's scope.** `_eventDispatcher` is the scoped `IEventDispatcher` of
whoever called `CommitAsync`, so the handlers it resolves resolve `IContext` from that same scope — a context
already built (or about to be built) from the *committing request's* inbound state. The documented mechanism
assumed the dispatch activity would fix this by itself: because the activity is parented into the entry's trace,
"any context created underneath it picks the right identity up through the normal sourcing rules". No context is
created underneath it — the committing scope already has one.

That produces two distinct failures.

### 1. Entries dispatched under another request's identity

`IOutboxCommit.CommitAsync()` retrieval is global across scopes — deliberately, and documented as such. With the
canonical per-request commit shown in `docs/docs/outbox.mdx`, request A's commit dispatches whatever request B
stored. B's handler then logs A's `TraceId` through `LoggingEnrichmentBehavior` and sees A's baggage, while B's
own baggage is silently gone. The `Trace-Id` A returned to its client points at work it did not cause, and B's
flow has a hole exactly where the outbox was supposed to bridge it.

### 2. Nothing propagated at all when no `ActivityListener` is registered

This is a configuration the library explicitly supports — it is why `ContextIdentity` mints a trace id as a last
resort, and why known issue 040 made capture write `traceparent` from the context. With no listener,
`StartActivity` returns `null`. The stored trace context then influences nothing whatsoever: no span is created
to carry it, and the context the handlers read is the caller's. The entry's headers are written, persisted and
parsed for no effect.

---

## Steps to reproduce

1. Register Synapse with the in-memory outbox and **no** `ActivityListener` (no OpenTelemetry wiring).
2. In one scope, set `tenant.id` baggage on `IContext` and `EmitAsync(evt, EmitMode.Outbox)`.
3. From a **different** scope, whose context has its own trace id, call `IOutboxCommit.CommitAsync()`.
4. In the event handler, read `IContext.TraceId` and `IContext.Baggage`.

---

## Expected behavior

The handler's `IContext` carries the flow the entry was stored in: the storing scope's trace id, the storing
span as `CausationId`, and the baggage that was captured with the entry.

---

## Actual behavior

The handler's `IContext` is the committing scope's: the committing request's trace id, and none of the entry's
baggage. With no `ActivityListener` the stored `traceparent` has no effect on anything.

---

## Code sample

```csharp
// Storing scope
context.SetBaggage("tenant.id", "contoso");
await emitter.EmitAsync(new MailRequested(id), EmitMode.Outbox, ct);
var storedTraceId = context.TraceId;

// A different scope commits
await outboxCommit.CommitAsync(ct);

// In the handler
public ValueTask<Result> HandleAsync(MailRequested e, CancellationToken ct)
{
    _context.TraceId;                  // the committing scope's id, not storedTraceId
    _context.Baggage["tenant.id"];     // KeyNotFoundException
    return ValueTask.FromResult(Result.Success());
}
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

Dispatching an entry was treated as a continuation of the caller's unit of work rather than as a unit of work of
its own. A scope's context is built exactly once, from `IInboundContextStore`, and can never be re-identified
afterwards — that immutability is deliberate (see known issue 029). So restoring a *different* flow into a scope
that already has one is not possible by design: the only place the entry's flow can be applied is a scope that
does not have a context yet.

Parenting the dispatch activity looked like it covered this, and does cover the *tracing* half — the spans land
in the right trace. It cannot cover the context half, because no new context is created during the dispatch, and
it covers nothing at all when no listener is registered.

### Resolution

`OutboxManager` now takes an `IServiceScopeFactory` instead of an `IEventDispatcher`, and
`DispatchEventAsync` dispatches each entry in a scope of its own:

```csharp
await using var scope = _scopeFactory.CreateAsyncScope();

scope.ServiceProvider.GetRequiredService<IInboundContextStore>()
    .Inbound = restored;

var result = await dispatcher(@event,
    scope.ServiceProvider.GetRequiredService<IEventDispatcher>(),
    cancellationToken);
```

The store is written before anything in the scope resolves `IContext`, which is what materializes it — nothing
has, the scope being one statement old. `ContextIdentity.ForUnitOfWork` then takes the trace id and causation id
from `restored.Trace`, and `ContextBaggage.Restore` replays `restored.Baggage`, so the handlers see the flow the
entry was filed under whether or not any tracing is wired. The scope is created inside the dispatch activity's
`using`, so an entry with no stored trace context still identifies itself from that activity rather than from
the caller.

Consequences worth knowing, now documented in `docs/docs/outbox.mdx` and `docs/docs/propagation.mdx`:

- handlers no longer share the committing request's scoped services — a `DbContext` they resolve is a new one,
  which is consistent with the entry having been stored precisely so its handling happens after the transaction
  committed;
- the caller's own flow is untouched, and entries no longer borrow each other's identity.

**Verification.** Two tests added to `OutboxFlowIdentityTests`:
`Dispatch_BuildsTheHandlersContextFromTheStoredEntry` runs with no `ActivityListener` registered and asserts the
dispatch scope's `IContext` carries the storing context's trace id and its `tenant.id` baggage, and *not* the
committing context's trace id; `Dispatch_OfAnEntryStoredByAnotherFlow_DoesNotAdoptTheCommittingFlowsIdentity`
stores two entries in two different flows, commits from a third, and asserts each is dispatched under its own
trace id and baggage. The existing flow-identity and leak tests still pass. `dotnet build -c Release` clean;
full suite 687 passed / 0 failed.
