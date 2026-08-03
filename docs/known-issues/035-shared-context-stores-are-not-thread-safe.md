# [Bug]: The shared context's baggage and feature stores are not thread-safe

**Severity:** High
**Area:** Core DI
**Discovered on:** `main`, .NET 10, code review of the v2 trace-context rework
**Status:** ✅ **Resolved** on `main` — see [Resolution](#resolution).

> **TL;DR.** `Context` held its baggage and features in plain `Dictionary` instances, but
> `ConcurrentEventOrchestrator` runs every handler for an event against the one shared context, so two handlers
> setting baggage corrupted the dictionary and desynced the byte counter, and a handler writing while the
> propagator enumerated threw `InvalidOperationException`.

---

## Describe the bug

`Context` is a scoped, shared instance — its own XML docs stress that every consumer holds the same object — and
`ConcurrentEventOrchestrator` publishes an event to all of its handlers with `Task.WhenAll`:

```csharp
// src/Synapse/Publish/Orchestrators/ConcurrentEventOrchestrator.cs
await Task.WhenAll(handlers.Select(handler => handler(@event, cancellationToken).AsTask()));
```

Every one of those handlers resolves the same `IContext`. Both mutable stores behind it were unsynchronized:

```csharp
// src/Synapse/Contexts/Context.cs — before
private readonly Dictionary<Type, IContextFeature> _features;

// src/Synapse/Contexts/BaggageCollection.cs — before
private readonly Dictionary<string, string> _entries;
private int _totalBytes;
```

Three distinct failures followed:

1. **Lost and corrupted entries.** Two concurrent `Dictionary` writes can lose an entry outright or leave the
   bucket chain inconsistent, which surfaces later as a phantom missing key or an infinite loop on lookup.
2. **A byte counter that drifts.** `_totalBytes` is a read-modify-write across the whole collection. Racing
   writers lose increments, so the counter stops describing the content it guards — and because it is the input
   to the W3C size check, baggage silently starts rejecting entries that would have fitted (or accepting ones
   that push the header past 8192 bytes).
3. **Enumeration throwing.** `Context.Baggage` hands out the live dictionary, and both
   `W3CContextPropagator.Inject` and `LoggingEnrichmentBehavior.CreateState` enumerate it. A sibling handler
   adding or removing an entry mid-enumeration throws
   `InvalidOperationException: Collection was modified; enumeration operation may not execute` — from the
   propagation path, i.e. while writing outbound headers.

Features are exposed to the same race: the CQRS boundary marker is set and removed per request through
`SetFeature`/`RemoveFeature`.

---

## Steps to reproduce

1. Register two or more handlers for one event.
2. Have each call `context.SetBaggage(...)` (or `SetFeature`).
3. Publish the event with the concurrent orchestrator, repeatedly.

---

## Expected behavior

Concurrent handlers may read and write context baggage and features freely: every accepted entry is kept, the
byte counter always describes exactly what is stored, and an enumeration in progress never throws.

---

## Actual behavior

Intermittently: entries missing, `SetBaggage` refusing entries that fit, or
`InvalidOperationException: Collection was modified` thrown from `Inject` or from the logging scope.

---

## Code sample

```csharp
var context = new Context(identity);

Parallel.For(0, 32, i => context.SetBaggage($"tenant.{i}", $"value-{i}"));

// before: fewer than 32 entries, and/or a _totalBytes that no longer matches the content
Console.WriteLine(context.Baggage.Count);
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

The context was designed as one shared object and the event pipeline was designed to run handlers concurrently,
but the stores inside the context were written as if a single thread owned them.

### Resolution

Both stores became copy-on-write over a shared empty dictionary: a writer copies the published dictionary,
changes the copy, and swaps the reference in, so readers are lock-free and an enumeration holds a snapshot
nobody will mutate.

- `BaggageCollection` keeps a lock for writers, because the size check spans the dictionary and the byte
  counter and the two have to move together. It also gained `SetRange`, used by `ContextBaggage.Restore`, so
  applying inbound baggage is one copy rather than one per entry.
- `Context`'s features need no lock — there is no cross-field invariant — so writes are a
  `Interlocked.CompareExchange` retry loop.

Copy-on-write was chosen over `ConcurrentDictionary` on allocation grounds. Measured with
`benchmarks/SynapseBenchmark` (`PropagationBenchmarks`, net10.0), a `ConcurrentDictionary` per store costs about
900 B the first time it is written, against 216 B for the whole of context creation:

| Benchmark | `ConcurrentDictionary` | Copy-on-write |
| --- | --- | --- |
| `CreateContext_Root` | 37.2 ns / 216 B | 38.9 ns / 216 B |
| `CreateContext_FromInboundTrace` | 256.8 ns / 1240 B | 114.9 ns / 488 B |
| `SetBaggage_FirstEntry` | 143.7 ns / 1136 B | 77.0 ns / 432 B |
| `SetFeature_ThenRead` | 134.9 ns / 1160 B | 77.6 ns / 480 B |
| `SetBaggage_Overwrite_ThenRead` | 39.6 ns / 0 B | 57.9 ns / 216 B |

The one regression is the overwrite path: copy-on-write allocates a copy per write where the previous code
mutated in place. With the handful of baggage writes a unit of work actually makes, that is a smaller cost than
one eager concurrent store, and it buys allocation-free reads on the boundary paths that run on every message.

**Verification.** `test/Synapse.Tests/Contexts/ContextTests.cs` —
`SetBaggage_FromConcurrentHandlers_KeepsEveryEntryAndItsByteCount`,
`SetBaggage_AndRemoveBaggage_UnderConcurrentChurn_KeepTheByteCounterExact`,
`Baggage_EnumeratedWhileAnotherHandlerWrites_DoesNotThrow` and
`SetFeature_FromConcurrentHandlers_LandsInOneStore`, all driven through the `Race` harness
(`test/Synapse.Tests/Definitions/Race.cs`) so the workers start together on dedicated threads. Against the
previous implementation two or three of those four fail on every run. The batch restore path is pinned by
`DefaultContextFactory_Create_WithInboundBaggage_LeavesTheByteBudgetExact` and
`DefaultContextFactory_Create_WithMoreInboundEntriesThanTheCap_KeepsTheCap`.
