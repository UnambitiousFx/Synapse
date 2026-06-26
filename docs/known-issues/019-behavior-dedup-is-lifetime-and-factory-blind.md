# [Bug]: Behavior dedup is lifetime-blind and ignores factory/instance registrations

**Severity:** Medium
**Area:** Core DI
**Discovered on:** `feature/typed-pipeline-behaviors`, .NET 10
**Status:** ✅ **Resolved** — discovered in code review.

---

## Describe the bug

`BehaviorAlreadyRegistered` deduplicates pipeline-behavior registrations by comparing only
`descriptor.ServiceType == serviceType && descriptor.ImplementationType == implementationType`. Two
problems follow:

1. **Lifetime-blind.** The same closed behavior over the same request registered a second time with a
   *different* `ServiceLifetime` is silently dropped — the first registration wins. A behavior the
   user intended as `Singleton` may run as the generator's default `Scoped`, or vice-versa.

2. **Factory/instance-blind.** Descriptors created from an implementation factory or instance have a
   `null` `ImplementationType`, so they never match the guard and are never deduped. The dedup is
   **load-bearing** for `CqrsBoundaryEnforcementBehavior`, which is non-idempotent (a second instance
   sees the boundary marker the first one set and throws on every request). A duplicate registered via
   a factory/instance bypasses the guard and reintroduces that failure.

Separately, this hand-rolled guard coexists with `RegisterValidator`, which dedups the validation
behavior via `TryAddEnumerable` — two different dedup mechanisms for the same "register at most once"
problem, both of which ignore lifetime.

---

## Steps to reproduce

1. Register a closed behavior twice with two different lifetimes (e.g. generator default `Scoped`,
   plus a manual `Singleton`). Observe that only the first lifetime takes effect.
2. (CQRS) Register `CqrsBoundaryEnforcementBehavior<T>` once by type and once via an implementation
   factory. Observe that both are added and every request throws.

---

## Expected behavior

Registrations that are genuinely the same behavior are deduped regardless of how the descriptor was
constructed; a lifetime conflict is either reconciled deterministically or surfaced.

---

## Actual behavior

A second registration with a different lifetime is silently discarded; a factory/instance duplicate is
not deduped at all.

---

## Root cause

`src/Synapse/DependencyInjectionExtensions.cs` — `BehaviorAlreadyRegistered` (≈ line 195):

```csharp
if (descriptor.ServiceType == serviceType && descriptor.ImplementationType == implementationType)
```

No lifetime comparison; `ImplementationType` is `null` for factory/instance descriptors.

---

## To address

- Decide and document the dedup key (type identity only is reasonable) and, on a lifetime conflict,
  either throw or pick deterministically — do not silently drop.
- Account for factory/instance descriptors, or constrain the registration surface so behaviors are
  always added by implementation type.
- Consider unifying the validator path and the behavior path onto one dedup mechanism.

## Resolution

`src/Synapse/DependencyInjectionExtensions.cs`:

- The hand-rolled `BehaviorAlreadyRegistered` guard was removed. Every behavior and CQRS registration
  now goes through `TryAddEnumerable`, the same mechanism the validator path already used — one dedup
  mechanism for both paths (behaviors resolve as `IEnumerable<…>`, so this is a no-op for resolution).
- Dedup is keyed on `(service type, effective implementation type)`, where the implementation type is
  resolved by a new `EffectiveImplementationType` helper that mirrors the framework's internal
  `ServiceDescriptor.GetImplementationType`: it reads `ImplementationType`, else the instance's runtime
  type, else a typed factory's declared return type. Factory/instance descriptors are therefore no
  longer skipped — the non-idempotent `CqrsBoundaryEnforcementBehavior` is correctly deduped however the
  descriptor was built.
- A lifetime conflict (same behavior + service type already registered with a different
  `ServiceLifetime`) now throws `InvalidOperationException` naming the behavior and both lifetimes
  instead of silently dropping. The builder surface only ever registers `Scoped`, so this surfaces a
  conflict introduced by user code on the raw `IServiceCollection`.

**Known limitation:** a factory registered via the raw `ServiceDescriptor(Type, Func<…, object>, …)`
ctor declares an `object` return type, so its implementation type is undeterminable without invoking
it — such a descriptor is neither deduped nor conflict-detected. This matches the framework's own
`TryAddEnumerable` behavior. Typed factories, instances, and by-type registrations are all handled.

Regression tests in `test/Synapse.Tests/PipelineBehaviorDeduplicationTests.cs`:
`Behavior_preregistered_via_typed_factory_is_deduplicated`,
`Cqrs_behavior_preregistered_via_factory_runs_once`,
`Behavior_registered_with_conflicting_lifetime_throws`.

## Library version

`feature/typed-pipeline-behaviors` (pre-release)

## .NET version

.NET 10.0
