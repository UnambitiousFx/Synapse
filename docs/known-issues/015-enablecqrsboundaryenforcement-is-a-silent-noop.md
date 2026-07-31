# [Bug]: `EnableCqrsBoundaryEnforcement()` is a silent no-op

**Severity:** High
**Area:** Core DI / CQRS enforcement
**Discovered on:** `feature/typed-pipeline-behaviors`, .NET 10
**Status:** ✅ **Resolved** — the method now fails loudly instead of silently dropping enforcement.

---

## Describe the bug

`ISynapseConfig.EnableCqrsBoundaryEnforcement(bool)` no longer registers anything. It is marked
`[Obsolete]` and its body is an empty no-op; CQRS boundary enforcement is now emitted by the source
generator **only** when the assembly is decorated with `[assembly: EnableSynapseCqrsBoundaryEnforcement]`.

Any application that previously relied on the runtime call to obtain enforcement — and does not add
the new assembly attribute — silently loses all CQRS boundary protection. `[Obsolete]` produces a
compiler **warning**, which is frequently suppressed in library/host projects, so the regression can
ship unnoticed.

---

## Steps to reproduce

1. Upgrade to `feature/typed-pipeline-behaviors`.
2. Keep an existing composition root that calls `cfg.EnableCqrsBoundaryEnforcement(true)` and does
   **not** add `[assembly: EnableSynapseCqrsBoundaryEnforcement]`.
3. Dispatch a command from inside another command handler (a nested send).

---

## Expected behavior

Either enforcement is still active (nested send throws `CqrsBoundaryViolationException`), or the call
fails loudly (e.g. throws at configuration time) so the loss of behavior cannot pass silently.

---

## Actual behavior

The nested send succeeds. Boundary violations that previously threw are now allowed through, with no
runtime signal — only a (often-suppressed) build warning.

---

## Root cause

`src/Synapse/SynapseConfig.cs` (≈ line 346): `EnableCqrsBoundaryEnforcement` returns `this` without
registering the closed `CqrsBoundaryEnforcementBehavior<…>` descriptors. The runtime open-generic
registration path it used to perform was removed in favor of generator-emitted closed registrations
gated on the assembly attribute.

---

## To address

- Make the obsolete method **throw** (e.g. `NotSupportedException` with the migration message) rather
  than silently no-op, so the behavior change is impossible to miss; or
- Honor the runtime call by registering enforcement at config time as well; or
- At minimum, emit a startup diagnostic when the method is called but the assembly attribute is
  absent.

## Resolution

`EnableCqrsBoundaryEnforcement(bool)` no longer silently no-ops:

- The `[Obsolete]` attribute on both the interface (`src/Synapse/ISynapseConfig.cs`) and the
  implementation (`src/Synapse/SynapseConfig.cs`) is now `error: true`, so any caller fails at
  **compile time** — it can no longer be lost to a suppressed warning.
- The body throws `NotSupportedException` (with the migration message pointing at
  `[assembly: EnableSynapseCqrsBoundaryEnforcement]`) when called with `enable: true`, guarding
  reflection / late-bound / diagnostic-suppressed callers at **runtime**. `enable: false` returns
  silently, since enforcement is already off by default.

Restoring the runtime registration path was rejected: it would reintroduce
[001](001-open-generic-pipeline-behavior-aot-value-type.md) (open-generic behavior over value-type
responses under AOT) and duplicate the generator-emitted closed registrations.

**Update (v2):** `EnableCqrsBoundaryEnforcement(bool)` was deleted outright, along with the
`[assembly: EnableSynapseCqrsBoundaryEnforcement]` attribute the message above pointed at. Enable
enforcement globally with `[assembly: SynapseGlobalBehavior(typeof(CqrsBoundaryEnforcementBehavior<>))]`
and its with-response variant, or per request with `cfg.RegisterCqrsBoundaryEnforcement<…>()`. See the
"Migrating to v2" doc page.

## Library version

`feature/typed-pipeline-behaviors` (pre-release)

## .NET version

.NET 10.0
