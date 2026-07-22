# [Bug]: Generated `RegisterGroup` namespace ignores MSBuild `RootNamespace`

**Severity:** Medium
**Area:** Generator
**Discovered on:** `main`, .NET 8/9/10, project whose assembly name differs from its `RootNamespace`
**Status:** ✅ **Resolved** on `main` — see [Resolution](#resolution).

> **TL;DR.** The generator derived the `RegisterGroup` namespace from the assembly name instead of the
> MSBuild `RootNamespace`; it now reads `build_property.RootNamespace` first.

---

## Describe the bug

The source generator emits `RegisterGroup.g.cs` with `namespace {rootNamespace};`. The namespace was
resolved from assembly metadata only (an `AssemblyDefaultAliasAttribute` probe, then
`Compilation.AssemblyName`) and never from the MSBuild `RootNamespace` property. When a project's
assembly name differs from its `RootNamespace`, the generated class landed in the wrong namespace.

---

## Steps to reproduce

1. Create a project where the assembly name and root namespace differ, e.g. `Worker.csproj` with:
   ```xml
   <RootNamespace>Contoso.Billing.Worker</RootNamespace>
   ```
   (assembly name defaults to `Worker`).
2. Add a handler and reference `UnambitiousFx.Synapse.Generator`.
3. Inspect the generated `RegisterGroup.g.cs`.

---

## Expected behavior

The generated class is placed in `Contoso.Billing.Worker` (the project's `RootNamespace`), matching the
rest of the project's types.

---

## Actual behavior

The generated class is placed in `Worker` (the assembly name):

```csharp
namespace Worker;

public sealed class RegisterGroup : ... { ... }
```

---

## Code sample

```xml
<PropertyGroup>
    <RootNamespace>Contoso.Billing.Worker</RootNamespace>
</PropertyGroup>
```

```csharp
// Generated (before): namespace Worker;
// Generated (after):  namespace Contoso.Billing.Worker;
```

---

## Library version

`main`

## .NET version

.NET 8.0 / 9.0 / 10.0

## Operating system

macOS (also reproducible on Windows / Linux — SDK-independent)

---

## Additional context

### Root cause

`CompilationExtensions.GetRootNamespaceFromAssemblyAttributes` looked for
`System.Reflection.AssemblyDefaultAliasAttribute` (which is **not** populated from MSBuild
`RootNamespace`) and otherwise fell back to `Compilation.AssemblyName`. The MSBuild `RootNamespace`
is surfaced to source generators as `build_property.RootNamespace` via
`AnalyzerConfigOptionsProvider.GlobalOptions`, but the generator never read analyzer config options.

### Resolution

`SynapseGenerator.Initialize` now resolves the fallback namespace by combining
`AnalyzerConfigOptionsProvider` with the compilation: it prefers `build_property.RootNamespace` and
only falls back to `GetRootNamespaceFromAssemblyAttributes` (assembly-alias attribute → assembly name)
when the property is absent. Backward-compatible: projects where the assembly name equals the root
namespace are unchanged.

(A related enhancement lets users declare a `[RegisterGroup] partial class` to control the generated
class's namespace, name, and accessibility directly — see the Source Generator docs.)

**Verification.** New generator unit tests
`Generate_WithRootNamespaceProperty_UsesRootNamespace` and
`Generate_WithoutRootNamespaceProperty_FallsBackToAssemblyName` in
`test/Synapse.Generator.Tests/GeneratorBehaviorTests.cs` (feed `build_property.RootNamespace` through a
test `AnalyzerConfigOptionsProvider`); full generator suite passes (63 tests).
