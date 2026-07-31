# [Bug]: Outbox lookup matches the first value-equal event across all scopes

**Severity:** High
**Area:** Outbox (`InMemoryEventOutboxStorage`)
**Discovered on:** `feature/typed-pipeline-behaviors`, .NET 10
**Status:** ✅ **Resolved** — stored items now carry a stable `Guid` identity; lifecycle operations address items by id.

> **Note:** the correlation-scoped storage this report describes (`_scopedItems`) is gone. The v2
> context-propagation work replaced it with a single flat item collection, so the "two different scopes"
> framing below is historical — the ambiguity it describes can no longer arise from partitioning, only
> from duplicate value-equal events, which id-based lookup already handles.

---

## Describe the bug

`TryFindItem` now matches stored items with value equality (`i.Event.Equals(@event)`) **and** scans
`_scopedItems.Values` across **every** correlation scope (lifecycle operations are global by design —
see the class XML doc). Because Synapse events are typically records (value equality), two distinct
pending items holding equal-by-value events are indistinguishable. `MarkAsProcessedAsync`,
`MarkAsFailedAsync`, and `GetAttemptCountAsync` resolve to the **first** value-equal item found in
dictionary-enumeration order, not the caller's intended instance.

This is the inverse failure mode of [010](010-outbox-lookup-uses-reference-equality.md): the
reference-equality fix restored value equality, but the simultaneous switch from a per-correlation
lookup to a global `_scopedItems.Values.SelectMany` removed the scope guard that previously
disambiguated identical events originating from different requests.

---

## Steps to reproduce

1. From two different scopes (or the same scope twice), enqueue two events that are value-equal:

   ```csharp
   await storage.AddAsync(new TaskCompletedEvent(TaskId: 42)); // scope A
   await storage.AddAsync(new TaskCompletedEvent(TaskId: 42)); // scope B
   ```

2. Process and mark one of them:

   ```csharp
   await storage.MarkAsProcessedAsync(new TaskCompletedEvent(TaskId: 42));
   ```

---

## Expected behavior

The specific stored item the caller is processing is the one marked; the other remains pending and is
dispatched independently.

---

## Actual behavior

`TryFindItem` returns whichever value-equal item enumerates first. One item is marked Processed while
the other stays pending and is re-dispatched (duplicate dispatch); under different orderings an item
can be marked without ever being sent (dropped dispatch). No error surfaces.

---

## Root cause

`src/Synapse/Publish/Outbox/InMemoryEventOutboxStorage.cs` — `TryFindItem` (≈ line 165):

```csharp
foreach (var scopedItems in _scopedItems.Values)
{
    var foundItem = scopedItems.FirstOrDefault(i => i.Event.Equals(@event));
    ...
}
```

There is no stable per-item identity. Value equality + global scan = ambiguous match.

---

## To address

- Give each enqueued item a stable identity (e.g. a `Guid Id` on `Item`) and have the public storage
  contract address items by that id rather than by the event payload.
- Alternatively, return the item/handle from `AddAsync`/`GetPendingEventsAsync` and have the
  mark-as-* operations take it, so identity is explicit.
- Add a test enqueuing two value-equal events and asserting each can be marked independently.

## Resolution

Each stored outbox item now carries a stable `Guid Id` (`InMemoryEventOutboxStorage.Item.Id`). A new
`OutboxEntry(Guid Id, IEvent Event)` handle (in `Synapse.Abstractions`; the v2 propagation work later
added a third member, `IReadOnlyDictionary<string, string> Headers`, carrying the flow identity, and
`AddAsync` gained a matching `headers` parameter) is returned by
`GetPendingEventsAsync` / `GetDeadLetterEventsAsync`, and the lifecycle operations
(`MarkAsProcessedAsync`, `MarkAsFailedAsync`, `GetAttemptCountAsync`) now take that `Guid id` instead
of the event payload. `TryFindItem` matches on `i.Id == id`, so value equality no longer participates
in lookup — eliminating both the cross-scope and the same-scope duplicate-event ambiguity.
`IEvent` is unchanged (still a pure marker); identity is confined to the outbox. `OutboxManager`
threads the entry id through dispatch.

Regression test: `InMemoryEventOutboxStorageTests.MarkAsProcessedAsync_WithDuplicateValueEqualEvents_MarksEachItemIndependently`
enqueues two value-equal events and asserts each is marked independently.

## Library version

`feature/typed-pipeline-behaviors` (pre-release)

## .NET version

.NET 10.0
