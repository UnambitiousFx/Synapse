# Known Issues

Issues documented here were discovered during development and testing of the library.
Each file mirrors the [bug report template](.github/ISSUE_TEMPLATE/bug_report.yml) so it can be
filed as a GitHub issue with minimal editing.

| File | Summary | Status | Severity | Area |
|---|---|---|---|---|
| [001](001-open-generic-pipeline-behavior-aot-value-type.md) | Open-generic pipeline behavior registrations throw at runtime under Native AOT when `TResponse` is a value type | ✅ Resolved | **High** | Core DI |
| [002](002-validateonbuild-does-not-suppress-aot-open-generic-check.md) | `ValidateOnBuild = false` does not suppress the issue-001 runtime error | ✅ Resolved | Medium | Core DI / Docs |
| [003](003-authorization-failure-maps-to-http-500.md) | Pipeline short-circuit via `Result.Failure<T>()` was mapped to HTTP 500; behavior now returns a typed `UnauthorizedFailure` → 401 | ✅ Resolved | Medium | AspNetCore mapping |
| [004](004-validators-never-run-without-manual-behavior-registration.md) | `AddValidator` registers a validator that is never wired into the pipeline, so validation silently never runs | ✅ Resolved | **High** | Core DI / Validation |
| [005](005-generator-drops-enclosing-type-chain-for-nested-handlers.md) | Generator emits an uncompilable type name for nested handler/behavior classes (drops enclosing-type chain) | ✅ Resolved | **High** | Generator |
| [006](006-open-generic-behavior-closed-with-wrong-arity.md) | Open-generic behavior closed with the interface's arity, not the class's own → CS0305 | ✅ Resolved | **High** | Generator |
| [007](007-outbox-metrics-emit-nothing.md) | Outbox observable gauges are never registered; `Record*` are no-ops, so outbox metrics emit nothing | ✅ Resolved | **High** | Observability |
| [008](008-cross-assembly-behavior-registered-and-runs-twice.md) | Cross-assembly open-generic behavior is registered (and runs) twice — no dedup on the user-behavior path | ✅ Resolved | **High** | Generator / Core DI |
| [009](009-behavior-order-not-honored-across-registration-sources.md) | `Order` is only a generator-local sort; runtime order follows DI registration across sources | ✅ Resolved | Medium | Pipeline ordering |
| [010](010-outbox-lookup-uses-reference-equality.md) | Outbox event lookup narrowed to `ReferenceEquals`, breaking the public contract for equal-but-distinct events | ✅ Resolved | Medium | Outbox |
| [011](011-tracing-capture-stores-zero-trace-ids.md) | Tracing capture stores zero/invalid trace ids (empty-string guard never fires) and stringifies twice | ✅ Resolved | Medium | Observability |
| [012](012-globalizetype-breaks-on-tuple-and-pointer-types.md) | `GlobalizeType` emits invalid `global::` prefix for tuple / pointer / function-pointer types | ✅ Resolved | Medium | Generator |
| [013](013-invoker-overloads-resolve-handlers-inconsistently.md) | `Invoker` overloads resolve handlers inconsistently (static `TRequest` vs runtime `request.GetType()`) | ✅ Resolved | Medium | Core |
| [014](014-outbox-lookup-matches-value-equal-event-across-scopes.md) | Outbox lookup matched the first value-equal event across all scopes; stored items now carry a stable `Guid` identity surfaced via `OutboxEntry`, and lifecycle ops address items by id | ✅ Resolved | **High** | Outbox |
| [015](015-enablecqrsboundaryenforcement-is-a-silent-noop.md) | `EnableCqrsBoundaryEnforcement()` was an `[Obsolete]` no-op; runtime-API callers silently lost all enforcement. Now `[Obsolete(error: true)]` and throws `NotSupportedException` on `enable:true` | ✅ Resolved | **High** | Core DI / CQRS |
| [016](016-cqrs-boundary-marker-leaks-on-handler-exception.md) | CQRS boundary marker leaked when a handler threw → spurious violation on later send in the same scope; the marker is now cleared on the exception path (without masking the original exception) | ✅ Resolved | Medium | Pipeline / CQRS |
| [017](017-splittoplevelargs-ignores-tuple-parentheses.md) | `SplitTopLevelArgs` ignored tuple `()`, so a tuple nested as a generic argument emitted uncompilable `global::` code; request/target/event/behavior/validator types are now globalized at the symbol level via `ToEmitName` and the string parser was removed | ✅ Resolved | Medium | Generator |
| [018](018-cqrs-enforcement-skips-generator-invisible-handlers.md) | CQRS enforcement was emitted per-discovered-handler only; a public `ISynapseConfig.RegisterCqrsBoundaryEnforcement<…>` API now enforces manually/runtime-registered handlers (closed, AOT-safe, deduplicated) | ✅ Resolved | Medium | Generator / CQRS |
| [019](019-behavior-dedup-is-lifetime-and-factory-blind.md) | Behavior dedup compared `(ServiceType, ImplementationType)` only (lifetime-blind, skipped factory/instance); all behavior/CQRS registrations now unify on `TryAddEnumerable` keyed by effective implementation type, and a lifetime conflict throws | ✅ Resolved | Medium | Core DI |
| [020](020-outbox-failed-count-counts-retrying-events.md) | Outbox "failed count" counted retrying events (`Attempts > 0`), tripping the health check on transient retries; split into a `retrying` count and an operator-actionable `dead_letter` count, with the health check now keyed off dead-letters | ✅ Resolved | Medium | Outbox / Observability |
| [021](021-eventdispatcher-resorts-behaviors-every-publish.md) | `EventDispatcher` re-sorts behaviors via `OrderBy` on every publish (per-publish allocation) | ✅ Resolved | Low | Pipeline / Perf |
| [022](022-validator-attribute-picks-first-irequest-response.md) | `[Validator]` derived the response type from the first `IRequest<T>` found; multi-`IRequest` requests now emit diagnostic MDG011 and skip registration instead of guessing | ✅ Resolved | Low | Generator / Validation |
| [023](023-recordoutbox-methods-are-dead-noops.md) | `RecordOutbox*` metric methods were dead no-ops on `ISynapseMetrics` (superseded by observable gauges); removed | ✅ Resolved | Low | Observability |

> **Discovery context:** 001–003 were found while building the pipeline-behavior showcase in
> `examples/MinimalApi` on branch `feature/typed-pipeline-behaviors` against .NET 10 with
> `PublishAot=true`. 004–013 were found in a code review of the same branch
> (`git diff main...HEAD` plus working-tree changes) and verified against the source; they are now
> resolved. 014–023 were found in a later high-effort code review of the same branch (8 finder
> angles over the committed + working-tree diff, verified against the source); see each row for its
> current status. Line numbers in the individual files are approximate and may drift as the branch
> evolves.
