# [Bug]: `Order` is not honored across registration sources

**Severity:** Medium
**Area:** Core DI / Pipeline ordering
**Discovered on:** `feature/typed-pipeline-behaviors`, .NET 10
**Status:** ✅ **Resolved** on `feature/typed-pipeline-behaviors` — see [Resolution](#resolution).

---

## Describe the bug

`PipelineBehaviorAttribute.Order` is applied only as a **generator-local** sort within a single
`RegisterGroup`. At runtime the pipeline executes behaviors in **DI resolution order**. So `Order`
has no effect across:

- behaviors registered manually via `cfg.RegisterRequestPipelineBehavior<...>()`,
- behaviors emitted by a different `RegisterGroup` (another assembly),
- front-insertions (`RegisterRequestPipelineBehaviorFirst` and `RegisterCqrsBoundaryEnforcement`
  both call `Insert(0, ...)` and contend for the outermost slot).

The effective order then depends on the order of `AddRegisterGroup` / config calls, not on `Order`.

---

## Steps to reproduce

1. Declare two `[PipelineBehavior]` open generics with `Order = -100` and `Order = 100`.
2. Also register a behavior manually with `cfg.RegisterRequestPipelineBehavior<AuthBehavior, ...>()`
   expecting it to sit between them.
3. Dispatch a request and observe the actual execution order.

---

## Expected behavior

All behaviors for a request are ordered globally by `Order`, regardless of how/where they were
registered.

---

## Actual behavior

The manually registered behavior interleaves by call order, not by `Order`. Two `Insert(0, ...)`
registrations reverse each other — the last one wins the outermost slot, so CQRS boundary
enforcement may no longer wrap all behaviors.

---

## Root cause

- `ProxyRequestHandler` builds `_behaviors` from the injected
  `IEnumerable<IRequestPipelineBehavior<...>>` and runs them in DI order
  (`src/Synapse/ProxyRequestHandler.cs:39`).
- `Order` is applied only as `behaviors.OrderBy(b => b.Order)` inside one group
  (`src/Synapse.Generator/RegisterGroupFactory.cs:72`).
- Both `RegisterCqrsBoundaryEnforcement` and `RegisterRequestPipelineBehaviorFirst` use
  `services.Insert(0, ...)` (`src/Synapse/DependencyInjectionExtensions.cs:136` /
  `RegisterGroupFactory.cs` CQRS emit), with no cross-source ordering authority.

---

## To address

- Make `Order` a first-class, runtime-honored property: sort resolved behaviors by `Order` in
  `ProxyRequestHandler` (stable sort, ties by registration order), so ordering is independent of
  registration source.
- Give CQRS enforcement a reserved sentinel `Order` (e.g. `int.MinValue`) within that same ordering
  space instead of a separate `Insert(0)` regime.
- Add a test mixing generator, manual, and "first" registrations and asserting the resulting order.

## Resolution

Ordering is now a **runtime** concern with a single source of truth, so it no longer depends on
registration source or order.

**1. Runtime ordering contract.** New `IOrderedPipelineBehavior` (`uint Order`, with `First`/`Middle`/
`Last` bands) in `src/Synapse.Abstractions`. A behavior opts in by implementing it; a behavior that
does not is treated as `Last` (innermost).

**2. Stable sort at every pipeline entry.** `ProxyRequestHandler` (both variants),
`ProxyStreamRequestHandler`, and `EventDispatcher` now build their behavior list via
`behaviors.OrderBy(PipelineBehaviorOrdering.OrderOf)` (`src/Synapse/Pipelines/PipelineBehaviorOrdering.cs`).
LINQ `OrderBy` is stable, so behaviors that share an `Order` keep their registration order.

**3. No more front-insertion contention.** Both `RegisterCqrsBoundaryEnforcement` overloads switched
from `services.Insert(0, …)` to `services.Add(…)`; `CqrsBoundaryEnforcementBehavior` implements
`IOrderedPipelineBehavior` with `First` (outermost), and `RequestValidationBehavior` with `Last`
(innermost). The redundant `RegisterRequestPipelineBehaviorFirst` helpers and all `Insert(0, …)`
calls were removed.

**4. Compile-time `Order` removed.** `PipelineBehaviorAttribute.Order` is gone (the attribute is now
purely the discovery marker); the generator's Order extraction, `BehaviorDetail.Order`, and the
`OrderBy(b => b.Order)` in `RegisterGroupFactory` were dropped. Generated registrations are emitted in
a deterministic namespace+class-name order purely to keep generated source reproducible.

**Verification.** `test/Synapse.Tests/PipelineBehaviorOrderingTests.cs` registers behaviors from mixed
sources in scrambled order and asserts global execution order by `Order`, including a "First wraps a
default behavior registered before it" case. The generator emit-order test in
`test/Synapse.Generator.Tests/GeneratorBehaviorTests.cs` now asserts deterministic class-name ordering.

## Library version

`feature/typed-pipeline-behaviors` (pre-release)

## .NET version

.NET 10.0
