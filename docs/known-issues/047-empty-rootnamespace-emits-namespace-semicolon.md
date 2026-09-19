# [Bug]: An empty MSBuild `RootNamespace` makes the endpoints generator emit `namespace ;`

**Severity:** Medium
**Area:** Generator (`src/Synapse.Endpoints.Generator`)
**Discovered on:** `feat/synapse-endpoints`, .NET 10, whole-branch review
**Status:** ✅ **Resolved** on `feat/synapse-endpoints` — see [Resolution](#resolution).

> **TL;DR.** `build_property.RootNamespace` is *present but empty* when a project declares
> `<RootNamespace></RootNamespace>`, so the generator's `?? "…"` fallback never fired and all three
> generated files opened with a literal `namespace ;`; the property is now checked with
> `IsNullOrWhiteSpace` and falls back to the assembly name, matching the sibling generator.

---

## Describe the bug

`EndpointsGenerator` read `build_property.RootNamespace` and applied
`rootNamespace ?? "UnambitiousFx.Synapse.Endpoints.Generated"` at emit time. MSBuild surfaces a
declared-but-empty `RootNamespace` as an empty string, not as an absent option, so the analyzer config
lookup succeeds and `??` is never reached. Every emitted file then began with `namespace ;`.

The sibling `Synapse.Generator` already handled exactly this — issue
[025](025-registergroup-namespace-ignores-rootnamespace.md) is its half of the story — guarding with
`!string.IsNullOrWhiteSpace(...)` and falling back to
`CompilationExtensions.GetRootNamespaceFromAssemblyAttributes()`.

---

## Steps to reproduce

1. Build any project containing Synapse endpoints with an empty `RootNamespace`:

```bash
dotnet build examples/EndpointsApi -p:RootNamespace=
```

---

## Expected behavior

The build succeeds, with the generated code emitted into a namespace derived from the assembly.

---

## Actual behavior

`CS1001: Identifier expected` in each of `SynapseEndpointGroup.g.cs`,
`SynapseEndpointRegistrations.g.cs` and `SynapseEndpointBinders.g.cs`, plus a cascading `CS0234` where
the generated group type is referenced.

---

## Code sample

```csharp
// Before: a present-but-empty value is not null, so the fallback never fires.
var ns = rootNamespace ?? "UnambitiousFx.Synapse.Endpoints.Generated";
```

---

## Library version

`feat/synapse-endpoints` (pre-release; `Synapse.Endpoints.Generator` not yet published)

## .NET version

.NET 10.0 (generator project targets `netstandard2.0`)

## Operating system

macOS (Darwin), reproducible on any platform

---

## Additional context

### Root cause

`??` tests for absence; the failure mode is emptiness. The two are different states for an MSBuild
property surfaced through `AnalyzerConfigOptions`.

### Resolution

The root-namespace provider now combines the analyzer config options with the compilation, keeps the
MSBuild property only when `!string.IsNullOrWhiteSpace`, and otherwise falls back to a new
`CompilationExtensions.GetRootNamespaceFromAssemblyAttributes()` mirroring the sibling generator's —
the assembly name, a namespace a consumer would plausibly have typed, rather than a hardcoded one. A
final hardcoded fallback is retained only for an unnamed compilation, which would otherwise land back
on `namespace ;`. `Emit`'s parameter became non-nullable, so the hole cannot reopen silently.

An *invalid* identifier (`-p:RootNamespace=My-App`) still breaks both generators identically; that is
pre-existing and repo-consistent, and deliberately left alone.

**Verification.** `dotnet build examples/EndpointsApi -p:RootNamespace=` now succeeds with zero
warnings (it produced four errors before). Added generator tests for an empty and a whitespace-only
`RootNamespace` (asserting the assembly name is used and, explicitly, that `namespace ;` is absent) and
for a set `RootNamespace` still winning over the assembly name.
