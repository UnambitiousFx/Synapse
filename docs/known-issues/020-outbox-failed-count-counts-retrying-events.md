# [Bug]: Outbox "failed count" counts retrying events, tripping the health check on transient retries

**Severity:** Medium
**Area:** Outbox / Observability
**Discovered on:** `feature/typed-pipeline-behaviors`, .NET 10
**Status:** ✅ **Resolved** — discovered in code review; fixed by splitting retrying vs dead-letter counts.

---

## Describe the bug

`GetFailedCountAsync` counts every pending, non-dead-lettered item whose `Attempts > 0`. An event that
failed once and is scheduled for retry (`Processed = false`, `DeadLetter = false`, `Attempts = 1`)
is therefore counted as "failed". This conflates **currently retrying** with **failed/dead-lettered**.

The value feeds the `mediator.outbox.failed` gauge and `OutboxHealthCheck`, so normal transient
backpressure (events being retried) drives the failed metric up and can flip the health check to
Degraded/Unhealthy even though nothing has been dead-lettered.

---

## Steps to reproduce

1. Enqueue events and induce transient dispatch failures so they are retried (not dead-lettered).
2. Read `GetFailedCountAsync` / the failed gauge / the outbox health check.

---

## Expected behavior

"Failed" reflects events that have exhausted retries / been dead-lettered (operator-actionable), not
events that are merely mid-retry.

---

## Actual behavior

Each retrying event is counted as failed. With enough concurrent transient retries the failed count
crosses the health threshold and reports unhealthy despite zero dead-letters.

---

## Root cause

`src/Synapse/Publish/Outbox/InMemoryEventOutboxStorage.cs` — `GetFailedCountAsync` (≈ line 139):

```csharp
.Count(i => i is { Processed: false, DeadLetter: false } && i.Attempts > 0);
```

---

## To address

- Count `DeadLetter` items for the "failed" metric, or
- Rename this to a "retrying count" and add a separate dead-letter count, and align
  `OutboxHealthCheck` thresholds with the intended semantics.

## Resolution

The two concepts were split explicitly (second option above):

- `IEventOutboxStorage.GetFailedCountAsync` → renamed **`GetRetryingCountAsync`** (events that failed
  at least once and are awaiting retry — transient backpressure, predicate unchanged). A new
  **`GetDeadLetterCountAsync`** counts `DeadLetter` items (operator-actionable).
- `OutboxHealthCheck` now keys its Degraded/Unhealthy thresholds off the **dead-letter** count
  (options renamed `DegradedDeadLetterThreshold` / `CriticalDeadLetterThreshold`); the retrying count is surfaced in the
  result `data` (`retrying_count`, `dead_letter_count`) but never trips the status. Retrying events
  alone keep the outbox `Healthy`.
- Metrics: gauge `mediator.outbox.failed_count` → **`mediator.outbox.retrying_count`**, plus a new
  **`mediator.outbox.dead_letter_count`** gauge.

A regression test (`OutboxHealthCheckTests.CheckHealthAsync_WhenManyRetryingButNoDeadLetters_ReturnsHealthy`)
covers the original symptom.

## Library version

`feature/typed-pipeline-behaviors` (pre-release)

## .NET version

.NET 10.0
