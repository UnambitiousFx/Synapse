# [Bug]: CQRS enforcement only covers generator-discovered handlers

**Severity:** Medium
**Area:** `Synapse.Generator` / CQRS enforcement
**Discovered on:** `feature/typed-pipeline-behaviors`, .NET 10
**Status:** ✅ Resolved

---

## Resolution

An explicit runtime API was added (the second option below). `ISynapseConfig` now exposes two
public, AOT-safe overloads:

```csharp
ISynapseConfig RegisterCqrsBoundaryEnforcement<TRequest>()
    where TRequest : IRequest;

ISynapseConfig RegisterCqrsBoundaryEnforcement<TRequest, TResponse>()
    where TRequest : IRequest<TResponse>
    where TResponse : notnull;
```

(`src/Synapse/ISynapseConfig.cs`, implemented in `src/Synapse/SynapseConfig.cs`). Both delegate to
the existing internal `IServiceCollection.RegisterCqrsBoundaryEnforcement<…>` helpers
(`src/Synapse/DependencyInjectionExtensions.cs`) — the same **closed** (Native-AOT safe)
`CqrsBoundaryEnforcementBehavior<…>` registration the generator emits per discovered handler.

- **Covers both paths.** Unlike a runtime open-generic fallback, this works for the with-response
  path too — a value-type `TResponse` (`Guid`, `int`) cannot be closed open-generic under Native
  AOT, but a closed per-request registration can.
- **Deduplicated.** Registration is collapsed on `(ServiceType, ImplementationType)`, so calling
  it for a request the generator *also* covers is harmless — the (non-idempotent) behavior is wired
  at most once. Runs outermost via `IOrderedPipelineBehavior.First`.
- **Respects the cross-assembly opt-out.** No blanket wrapping of every `IRequest`.

**Contract:** manual handler registration requires a matching enforcement registration. A handler
registered via `cfg.RegisterRequestHandler<…>()` (or living in an assembly the generator does not
scan) gets enforcement only when the composition root also calls
`cfg.RegisterCqrsBoundaryEnforcement<…>()` for that request. Handlers the generator discovers under
`[assembly: SynapseGlobalBehavior(typeof(CqrsBoundaryEnforcementBehavior<>))]` (and its with-response
variant) remain wired automatically.

Covered by `test/Synapse.Tests/CqrsBoundaryEnforcementTests.cs`
(`Manual_enforcement_enforces_a_runtime_registered_handler_the_generator_cannot_see` and
`Manual_and_generated_enforcement_for_same_request_is_deduplicated`).

---

## Describe the bug

CQRS boundary enforcement was migrated from a runtime open-generic registration (an
`IRequestPipelineBehavior<,>` that wrapped **every** `IRequest` at resolve time) to **closed**
`RegisterCqrsBoundaryEnforcement<…>` calls emitted by the generator, one per discovered request
handler. As a consequence, any request whose handler the generator does **not** see gets no boundary
behavior — even when enforcement is "enabled" via the assembly attribute.

Generator-invisible handlers include those registered manually at runtime
(`cfg.RegisterRequestHandler<…>()` / direct `IServiceCollection` registration) and handlers in
assemblies the generator does not scan.

---

## Steps to reproduce

1. Enable enforcement with `[assembly: EnableSynapseCqrsBoundaryEnforcement]`.
2. Register a request handler **manually** at runtime rather than letting the generator discover it.
3. From inside that handler, dispatch another command (a nested send).

---

## Expected behavior

Enforcement applies to every request handled by the mediator, regardless of how the handler was
registered.

---

## Actual behavior

No `CqrsBoundaryEnforcementBehavior` is registered for the manually-registered request type, so the
nested send is not detected and does not throw.

---

## Root cause

The generator emits CQRS registrations only for handler types found in `behaviorTargets` / `details`
(the generator's discovered set) — `BuildCqrsBoundaryBehaviors` in
`src/Synapse.Generator/SynapseGenerator.cs`, whose output `RegisterGroupFactory` writes out. The old open-generic descriptor
covered all `IRequest` at resolve time; the per-handler emission narrows coverage to the discovered
handlers.

---

## To address

- Reinstate a runtime open-generic enforcement option for handlers the generator cannot see (the AOT
  concern that motivated closed registrations applies to value-type response closing — the no-response
  `IRequest` path may be safe to keep open-generic), or
- Provide an explicit runtime API to register enforcement for a specific request type, and document
  that manual handler registration also requires manual enforcement registration.

## Library version

`feature/typed-pipeline-behaviors` (pre-release)

## .NET version

.NET 10.0
