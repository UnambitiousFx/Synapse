# [Bug]: Duplicate stored header keys abort the whole outbox batch

**Severity:** Low
**Area:** Outbox
**Discovered on:** `main`, .NET 10, code review of the v2 trace-context rework
**Status:** ✅ **Resolved** on `main` — see [Resolution](#resolution).

> **TL;DR.** `DispatchEventAsync` rebuilt an entry's headers with
> `ToDictionary(…, StringComparer.OrdinalIgnoreCase)`, which throws on keys differing only by case, and it did so
> *outside* the `try` — so one such entry aborted the entire batch with nothing marked processed, failed or retried.

---

## Describe the bug

```csharp
// src/Synapse/Publish/Outbox/OutboxManager.cs — before
var restored = _propagator.Extract(new DictionaryPropagationCarrier(
    entry.Headers.ToDictionary(h => h.Key, h => h.Value, StringComparer.OrdinalIgnoreCase)));

try
{
    …
}
catch (Exception ex)
{
    await HandleDispatchFailureAsync(entry, ex.Message, cancellationToken);
    …
}
```

`OutboxEntry.Headers` is an `IReadOnlyDictionary<string, string>` handed over by an `IEventOutboxStorage`
implementation, which is a **public extension point**. The in-repo carrier builds case-insensitive dictionaries, so
the in-memory storage never produces a collision — but a persistent implementation that round-trips headers through a
case-sensitive column, a JSON blob, or a broker's property bag can legitimately return both `traceparent` and
`TraceParent`. `ToDictionary` with an `OrdinalIgnoreCase` comparer then throws
`ArgumentException: An item with the same key has already been added.`

Because the call sat before the `try`, nothing caught it:

- `HandleDispatchFailureAsync` never ran, so the entry was not marked failed and no retry was scheduled;
- the exception propagated out of `ProcessPendingAsync`, so the *remaining* entries of the batch were never
  attempted either.

Every subsequent poll re-read the same batch and threw at the same entry: one badly-shaped row stalls the outbox
indefinitely, and the entry that caused it is indistinguishable from the ones behind it because none of them was
touched.

---

## Steps to reproduce

1. Implement `IEventOutboxStorage` so that `GetPendingEventsAsync` returns an entry whose `Headers` contains two
   keys differing only in case.
2. Put a second, ordinary entry behind it.
3. Call `ProcessPendingAsync`.

---

## Expected behavior

The entry is dispatched — case-variant duplicates collapse, last value winning, as they would on a repeated
`IPropagationCarrier.Set` — and in any case a problem with one entry never prevents the rest of the batch from
being attempted or leaves an entry unmarked.

---

## Actual behavior

`ArgumentException` propagated out of `ProcessPendingAsync`. No entry in the batch was marked processed or failed,
and every later poll failed identically.

---

## Code sample

```csharp
var entry = new OutboxEntry(Guid.NewGuid(), new OrderPlaced(),
    new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["traceparent"] = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01",
        ["TraceParent"] = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01"
    });

// before: ArgumentException, from outside the try — the whole batch stops here
await manager.ProcessPendingAsync(ct);
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

Trusting the shape of data that crosses a public extension point, and doing the untrusted work outside the handler
that exists to contain per-entry failures.

### Resolution

The copy is now an explicit loop — last value wins, matching `DictionaryPropagationCarrier.Set` — and it happens
**inside** the `try`, so anything unexpected about an entry's stored state is handled as that entry's failure:

```csharp
private static Dictionary<string, string> ReadHeaders(OutboxEntry entry)
{
    var headers = new Dictionary<string, string>(entry.Headers.Count, StringComparer.OrdinalIgnoreCase);

    foreach (var header in entry.Headers)
    {
        headers[header.Key] = header.Value;
    }

    return headers;
}
```

Moving the restore and the activity start inside the `try` is the load-bearing half: the loop removes the known
throw, and the placement means the next unknown one costs one entry rather than the batch.

**Verification.** `test/Synapse.Tests/Publish/Outbox/OutboxFlowIdentityTests.cs` —
`Dispatch_WithCaseVariantDuplicateStoredHeaders_ProcessesTheWholeBatch` uses a storage stub that returns headers
verbatim, with an ordinary entry behind the offending one, and asserts both are dispatched and marked processed with
none marked failed. It throws `ArgumentException` against the previous implementation.
