# [Bug]: `RecordOutbox*` metric methods are dead no-ops on the public interface

**Severity:** Low
**Area:** Observability
**Discovered on:** `feature/typed-pipeline-behaviors`, .NET 10
**Status:** ✅ **Resolved** — the three `RecordOutbox*` members were removed from `ISynapseMetrics`
and `SynapseMetrics`.

---

## Describe the bug

After the [007](007-outbox-metrics-emit-nothing.md) fix wired outbox metrics through observable
gauges (`CreateObservableGauge` + the `ObserveOutbox*` callbacks), the older push-style methods
`RecordOutboxQueueDepth(int)`, `RecordOutboxProcessingLag(double)`, and `RecordOutboxFailedCount(int)`
were left as empty no-op bodies, and the corresponding members remain on `ISynapseMetrics` with
parameters (`count` / `lagSeconds`) that are never read.

The interface still advertises a push-based recording API that silently discards everything passed to
it — misleading to implementers and to any caller that computes and pushes a value expecting it to be
recorded.

---

## Steps to reproduce

1. Implement or call `ISynapseMetrics.RecordOutboxQueueDepth(n)` (or the lag / failed-count
   equivalents) expecting the value to surface as a metric.

---

## Expected behavior

The interface exposes only metric operations that do something; outbox depth/lag/failed values reach
the meter.

---

## Actual behavior

The values are silently dropped — the observable gauges read from `IEventOutboxStorage`, and the
`Record*` methods do nothing.

---

## Root cause

`src/Synapse/Observability/SynapseMetrics.cs` (≈ lines 145, 155, 165) — empty method bodies; the
corresponding `ISynapseMetrics` members (and their parameters) are dead.

---

## Resolution

Removed the three `RecordOutbox*` members from `ISynapseMetrics` and their no-op implementations in
`SynapseMetrics` — they had zero callers across src/examples/test/benchmarks, and the observable
gauge path (`ObserveOutbox*` reading from `IEventOutboxStorage`) fully supersedes them. The
gauge-backing fields (`_lastKnownQueueDepth`, `_lastKnownProcessingLagSeconds`,
`_lastKnownFailedCount`) are retained since the `Observe*` callbacks use them.

## Library version

`feature/typed-pipeline-behaviors` (pre-release)

## .NET version

.NET 10.0
