# [Bug]: Concurrent accessor reads mint diverging trace IDs

**Severity:** High
**Area:** Core DI
**Discovered on:** `main`, .NET 10, code review of the v2 trace-context rework
**Status:** ✅ **Resolved** on `main` — see [Resolution](#resolution).

> **TL;DR.** `ContextHandler` created its context lazily with no synchronization, so two concurrent handlers in
> one scope could each find the field empty, each build a context with its own freshly minted trace id, and each
> keep using the one it built — the identity divergence issue
> [029](029-scoped-icontext-snapshot-diverges-from-accessor.md) removed, reintroduced through the accessor.

---

## Describe the bug

```csharp
// src/Synapse/Contexts/ContextHandler.cs — before
public IContext Context
{
    get
    {
        if (_context is not null)
        {
            return _context;
        }

        _context = _contextFactory.Create(_inboundContextStore.Inbound);
        return _context;
    }
}
```

Read once, that is correct and cheap. Read twice at the same moment it is not: both callers see `null`, both call
`Create`, and each returns *its own* instance while only one wins the field write. With nothing propagated inbound
and no ambient activity — the ordinary in-process case — `ContextIdentity.ForUnitOfWork` mints a random trace id,
so the two contexts have **different** `TraceId` values.

`ConcurrentEventOrchestrator` makes that a normal occurrence rather than a curiosity: all handlers for an event
run against one scope with `Task.WhenAll`, and `OutboxManager.CaptureHeaders` reads the accessor from the same
scope. The result is one unit of work logging two trace ids, or an outbox entry whose stored `traceparent` names a
trace no log line mentions.

DI's own scoped-resolution lock does not help: callers reach the context through `IContextAccessor.Context`, not
through `sp.GetService<IContext>()`, and the accessor is what does the lazy build.

---

## Steps to reproduce

1. Register two or more handlers for one event, each injecting `IContextAccessor` (or `IContext`).
2. Publish the event with the concurrent orchestrator so both handlers start together.
3. Compare the `TraceId` each handler observes, repeatedly.

---

## Expected behavior

One context per scope, with one identity, no matter how many callers reach the accessor at once or from which
thread.

---

## Actual behavior

Intermittently two context instances with two different `TraceId` values, one of them detached from the field so
that later readers cannot see it.

---

## Code sample

```csharp
var handler = new ContextHandler(new DefaultContextFactory(), new InboundContextStore());

var contexts = new IContext[32];
Parallel.For(0, 32, i => contexts[i] = handler.Context);

// before: more than one distinct instance, and more than one distinct TraceId
Console.WriteLine(contexts.Select(c => c.TraceId).Distinct().Count());
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

Check-then-act on a field, in a type whose whole job is to be the single owner of a per-scope value, reached from
code paths that are explicitly concurrent.

### Resolution

Creation moved behind a lock with a double-check, and the field is read and published with
`Volatile.Read`/`Volatile.Write` so the fast path stays a plain field read:

```csharp
public IContext Context
{
    get
    {
        var context = Volatile.Read(ref _context) ?? Create();
        …
    }
}
```

The same read also publishes the context to the ambient slot introduced for issue
[033](033-propagation-handler-never-sees-the-request-context.md) when the current execution branch does not
already hold it. An `AsyncLocal` write does not cross into a sibling branch, so the handler that *did not* create
the context would otherwise find the slot empty and its outbound HTTP calls would stamp nothing. Only the creating
branch records a value to restore on dispose; a sibling branch's publish dies with the branch.

**Verification.** `test/Synapse.Tests/Contexts/ContextHandlerTests.cs` —
`Context_ReadConcurrently_ReturnsOneInstanceWithOneTraceId` races 32 threads through the accessor (via the `Race`
harness) and asserts a single instance and a single trace id; it fails on every run against the previous
implementation. `Context_ReadFromASiblingBranch_IsAlsoPublishedToThatBranchesAmbientSlot` covers the ambient
publish from a branch that did not create the context.
