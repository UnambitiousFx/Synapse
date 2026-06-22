# [Bug]: Cross-assembly open-generic behavior is registered twice and runs twice

**Severity:** High
**Area:** `Synapse.Generator` / Core DI
**Discovered on:** `feature/typed-pipeline-behaviors`, .NET 10
**Status:** ✅ **Resolved** on `feature/typed-pipeline-behaviors` — see [Resolution](#resolution).

---

## Describe the bug

Open-generic user pipeline behaviors are cross-producted against handlers discovered across the
**entire reference graph** (the generator scans `compilation.GlobalNamespace`, which includes
referenced assemblies). The user-behavior registration path uses `services.Add(...)` with **no
deduplication**, unlike the CQRS-enforcement path which guards with `CqrsBoundaryAlreadyRegistered`.

When two opted-in assemblies each emit a closed registration of the same open-generic behavior over
the same request type, the behavior is registered twice and therefore **executes twice** in a single
request pipeline.

---

## Steps to reproduce

1. Define an open-generic `[PipelineBehavior]` (e.g. a logging or metrics behavior) in a shared
   library, and a request type / handler visible to multiple assemblies.
2. Reference that library from two assemblies that both run the generator and both produce a
   `RegisterGroup`.
3. Register both groups in the composition root and dispatch the request.

---

## Expected behavior

The behavior runs once per request.

---

## Actual behavior

The behavior runs twice (double logging / double timing; for any non-idempotent behavior, duplicate
side effects).

---

## Root cause

- `RegisterRequestPipelineBehavior` uses `services.Add(new ServiceDescriptor(...))` with no dedup
  (`src/Synapse/DependencyInjectionExtensions.cs`, around the `RegisterRequestPipelineBehavior`
  body; `RegisterGroupFactory.cs:100` emits the call).
- The CQRS path, by contrast, guards duplicates via `CqrsBoundaryAlreadyRegistered`
  before `Insert(0, ...)`.
- `ExtractAllBehaviorTargets` runs `visitor.Visit(compilation.GlobalNamespace)`, which includes
  types from referenced assemblies, so the same closed behavior/request pair can be emitted by more
  than one assembly's `RegisterGroup`.

---

## To address

- Make user-behavior registration idempotent: dedup on
  `(ServiceType, ImplementationType, request type)` before `Add`, mirroring the CQRS guard, or use a
  `TryAdd`-style registry keyed by the closed behavior+request.
- Alternatively, constrain cross-assembly expansion so a given closed behavior is emitted by exactly
  one assembly (related to the cross-assembly propagation default and the
  `DisableSynapseCrossAssemblyBehaviorsAttribute` opt-out).
- Add a multi-assembly generator/DI test asserting a single registration per closed behavior.

## Resolution

User pipeline-behavior registration is now idempotent, mirroring the CQRS-enforcement path. The
generator still emits a per-`RegisterGroup` registration call (dedup is intentionally a DI-layer
concern, so cross-assembly emission stays harmless), but the runtime collapses duplicates.

**1. Shared dedup guard.** `CqrsBoundaryAlreadyRegistered` was generalized to
`BehaviorAlreadyRegistered(services, serviceType, implementationType)` in
`src/Synapse/DependencyInjectionExtensions.cs` — it scans the `IServiceCollection` for a descriptor
matching both `ServiceType` and `ImplementationType`. The two CQRS callers now use it.

**2. Guard on every behavior path.** All user-behavior registration helpers compute
`(serviceType, implementationType)`, early-return when `BehaviorAlreadyRegistered`, and only then
`Add`/`Insert(0, …)`: `RegisterRequestPipelineBehavior` (with/without response),
`RegisterEventPipelineBehavior`, and `RegisterStreamRequestPipelineBehavior`.
`(ServiceType, ImplementationType)` is a sufficient key — the service type encodes the request type
and the implementation type is the closed behavior. (Validators were already deduplicated via
`TryAddEnumerable`.)

> Note: the `*First` ordering helpers and all `Insert(0, …)` calls referenced in the original fix were
> later removed by issue [009](009-behavior-order-not-honored-across-registration-sources.md); ordering
> is now runtime-driven via `IOrderedPipelineBehavior`. The dedup guard described here is unchanged.

**Verification.** Covered by `test/Synapse.Tests/PipelineBehaviorDeduplicationTests.cs`: two identical
`IRegisterGroup`s register the same closed behavior; tests assert a single service descriptor survives
(with and without response) and that the behavior executes exactly once per request. Existing CQRS
dedup tests stay green after the guard rename.

## Library version

`feature/typed-pipeline-behaviors` (pre-release)

## .NET version

.NET 10.0
