# [Bug]: Outbox event lookup uses reference equality

**Severity:** Medium
**Area:** Outbox (`InMemoryEventOutboxStorage`)
**Discovered on:** `feature/typed-pipeline-behaviors`, .NET 10
**Status:** ✅ **Resolved** on `feature/typed-pipeline-behaviors` — see [Resolution](#resolution).

> **Superseded:** the value-equality fix below was replaced by issue
> [014](014-outbox-lookup-matches-value-equal-event-across-scopes.md), which took the option this
> Resolution rejected: stored items carry a stable `Guid` surfaced through `OutboxEntry`, and lifecycle
> operations address items by id (`MarkAsProcessedAsync(Guid id, …)`), not by event instance. Read the
> Resolution here as history, not as current behaviour.

---

## Describe the bug

`MarkAsProcessedAsync`, `MarkAsFailedAsync`, and `GetAttemptCountAsync` locate the stored outbox
item via a shared `TryFindItem` helper that matches with `ReferenceEquals(i.Event, @event)`. The
previous implementation matched by value equality (`item.Event.Equals(@event)`). Because
`IEventOutboxStorage` is a public contract, any caller that passes an equal-but-not-identical event
instance (e.g. one reconstructed or deserialized) now fails to find the item.

---

## Steps to reproduce

1. Add an event to the outbox.
2. From an external/custom outbox processor (or a test), construct a **new** event instance with the
   same values and call `MarkAsProcessedAsync(thatEvent)`.

---

## Expected behavior

The matching outbox item is found and marked processed (events are typically records with value
equality).

---

## Actual behavior

`TryFindItem` returns not-found, so the operation returns
`Result.Failure("...was not found in the outbox storage")`. The event is never marked processed and
is retried / dead-lettered indefinitely.

---

## Root cause

`src/Synapse/Publish/Outbox/InMemoryEventOutboxStorage.cs:169`:

```csharp
scopedItems.FirstOrDefault(i => ReferenceEquals(i.Event, @event))
```

The internal replay path happens to pass the same instance returned by `GetPendingEventsAsync`, so
identity matching succeeds there — which is why existing tests pass and the regression is masked.

---

## To address

- Match by value equality (`i.Event.Equals(@event)`), or better, look items up by a stable event id
  rather than by the event object at all.
- Add a test that marks an event processed using an equal-but-distinct instance.

## Library version

`feature/typed-pipeline-behaviors` (pre-release)

## .NET version

.NET 10.0

## Resolution

`TryFindItem` (`src/Synapse/Publish/Outbox/InMemoryEventOutboxStorage.cs`) now matches by **value
equality** (`i.Event.Equals(@event)`) instead of `ReferenceEquals`. Events are records, so
equal-but-distinct instances (reconstructed or deserialized) now locate their outbox item;
`object.Equals` falls back to reference equality for classes that don't override it, so no existing
caller regresses, and the internal replay path (same instance) still matches.

Lookup-by-stable-id was rejected at the time: `IEvent` is a bare marker with no id, and
`GetPendingEventsAsync` handed callers back only `IEvent` objects — they had no id to pass back — so it
was not viable without a contract change. Issue
[014](014-outbox-lookup-matches-value-equal-event-across-scopes.md) subsequently made that contract
change, because value equality turned out to be unsound across scopes.

Tests in `test/Synapse.Tests/Publish/Outbox/InMemoryEventOutboxStorageTests.cs`: added
`MarkAsProcessedAsync_WithEqualButDistinctInstance_FindsAndMarksItem` (the regression), and reworked
the former reference-equality test into
`MarkAsProcessedAsync_WithDuplicateValueEqualEvents_MarksOneAndLeavesOnePending` (two value-equal
events are indistinguishable by value, so marking one leaves exactly one pending).
