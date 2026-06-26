# [Bug]: Outbox metrics emit nothing (gauges never registered)

**Severity:** High
**Area:** Observability
**Discovered on:** `feature/typed-pipeline-behaviors`, .NET 10
**Status:** ✅ **Resolved** on `feature/typed-pipeline-behaviors` — see [Resolution](#resolution).

---

## Describe the bug

The `SynapseMetrics` rewrite removed the observable-gauge registration for the outbox metrics but
did not replace it. The new `ObserveOutbox*` callback methods are never passed to
`CreateObservableGauge`, and the `RecordOutbox*` methods are empty no-ops. As a result, every
outbox metric (queue depth, processing lag, failed/dead-letter count) silently emits no data, with
no compile or runtime error.

---

## Steps to reproduce

1. Configure Synapse with the outbox enabled and wire a metrics listener (OpenTelemetry / a
   `MeterListener`) to the Synapse meter.
2. Enqueue events to the outbox and inspect the exported metrics.

---

## Expected behavior

The outbox gauges (e.g. `mediator.outbox.queue_depth`, processing lag, failed count) report current
values, so dashboards and alerts on outbox backlog and dead-letter accumulation work.

---

## Actual behavior

The outbox metric series report no data and flatline. Backlog growth and dead-letter accumulation
are invisible in production monitoring.

---

## Root cause

In `src/Synapse/Observability/SynapseMetrics.cs`:

- The previous constructor registered the gauges via
  `meter.CreateObservableGauge("mediator.outbox.queue_depth", ObserveEventOutboxQueueDepth, ...)`.
  That registration was deleted — a repository-wide search finds **zero** `CreateObservableGauge`
  calls remaining in `src/`.
- `ObserveOutboxQueueDepth` / `ObserveOutboxProcessingLag` / `ObserveOutboxFailedCount` are private
  methods that are never wired to any gauge.
- `RecordOutboxQueueDepth` / `RecordOutboxProcessingLag` / `RecordOutboxFailedCount` have empty
  bodies, commented "recorded via observable gauge, no manual recording needed" — but no gauge
  exists.

---

## To address

- Re-register the three observable gauges in the `SynapseMetrics` constructor, passing the
  `ObserveOutbox*` callbacks to `meter.CreateObservableGauge(...)`.
- Add a test using a `MeterListener` that asserts the outbox gauges produce measurements after the
  outbox is populated.

## Resolution

The three `ObserveOutbox*` callbacks already existed (with last-known-value fallback and a
read-failure counter); they were simply never wired to a gauge. The constructor now registers
them via `meter.CreateObservableGauge(...)` in `src/Synapse/Observability/SynapseMetrics.cs`:

- `mediator.outbox.queue_depth` → `ObserveOutboxQueueDepth` (unit `{event}`)
- `mediator.outbox.processing_lag` → `ObserveOutboxProcessingLag` (unit `s`)
- `mediator.outbox.retrying_count` → `ObserveOutboxRetryingCount` (unit `{event}`)
- `mediator.outbox.dead_letter_count` → `ObserveOutboxDeadLetterCount` (unit `{event}`)

The `RecordOutbox*` methods stay as documented no-ops (kept for interface compatibility); the
values flow through the observable gauges.

**Verification.** New `test/Synapse.Tests/Observability/SynapseMetricsTests.cs` uses a
`MeterListener` against the `Unambitious.Synapse` meter and calls
`RecordObservableInstruments()` to fire the callbacks:

- `OutboxGauges_AfterOutboxPopulated_ReportCurrentValues` — asserts the three gauges report the
  stubbed storage values (5 / 42s / 3).
- `OutboxGauges_WhenStorageThrows_FallBackAndCountReadFailure` — asserts fallback to last-known
  value (0) and that `mediator.outbox.metrics.read_failures` increments per read.
- `OutboxGauges_WithoutStorage_ReportZero` — asserts zero when no outbox storage is wired.

All three pass; full solution builds clean.

## Library version

`feature/typed-pipeline-behaviors` (pre-release)

## .NET version

.NET 10.0
