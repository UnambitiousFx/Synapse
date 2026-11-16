# [Bug]: The 3.4.0 generator fails every build on an SDK older than .NET 11 (CS9057)

**Severity:** **High**
**Area:** Source Generator
**Discovered on:** `v3.4.0`, consumer building with .NET SDK 10.0.404
**Status:** ✅ **Resolved** on `fix/generator-roslyn-floor` — see [Resolution](#resolution).

> **TL;DR.** `UnambitiousFx.Synapse.Generator` was built against the Roslyn of the newest SDK, so an older
> compiler refused to load it. It is now built against Roslyn 4.8 (.NET SDK 8.0.100), the oldest SDK the
> package supports, and a test keeps it there.

---

## Describe the bug

A dependency update (#201) moved `Microsoft.CodeAnalysis.CSharp` and `Microsoft.CodeAnalysis.Analyzers` from
5.9.0 to 5.12.0, the compiler of the .NET 11 SDK. The generator is loaded by the consumer's compiler, whatever
the project's target framework, and a compiler refuses an analyzer that references a newer Roslyn than itself.
Every project that references the generator then fails to build on .NET SDK 10 and older.

The same mismatch existed before at a smaller scale: each Roslyn bump dropped the SDKs older than the one it
came with, so a consumer on .NET SDK 8 or 9 could not use any recent version of the generator.

---

## Steps to reproduce

1. Use .NET SDK 10.0.404 (`global.json`).
2. Reference `UnambitiousFx.Synapse.Generator` 3.4.0 from a project with a handler.
3. `dotnet build`.

---

## Expected behavior

The project builds, as with 3.3.0.

---

## Actual behavior

```text
CSC : error CS9057: Analyzer assembly '…/unambitiousfx.synapse.generator/3.4.0/analyzers/dotnet/cs/UnambitiousFx.Synapse.Generator.dll'
cannot be used because it references version '5.12.0.0' of the compiler, which is newer than the currently running version '5.9.0.0'.
```

---

## Code sample

```xml
<PackageReference Include="UnambitiousFx.Synapse.Generator" Version="3.4.0" PrivateAssets="all" />
```

---

## Library version

`v3.4.0`

## .NET version

.NET SDK 10.0.404

## Operating system

macOS

---

## Additional context

### Root cause

The generator shared the solution-wide `Microsoft.CodeAnalysis.*` versions, which Renovate updates with every
other Microsoft package. The target framework plays no part: a `net8.0` project built with the .NET 11 SDK runs
Roslyn 5.12, the same project built with the .NET 8 SDK runs Roslyn 4.x. What matters is the compiler of the
oldest SDK a consumer may build with.

### Resolution

- `Synapse.Generator.csproj` references `Microsoft.CodeAnalysis.CSharp` 4.8.0 and `Microsoft.CodeAnalysis.Analyzers`
  3.11.0 through `VersionOverride`: the compiler of .NET SDK 8.0.100. The generator uses no newer API. The tests
  keep the solution-wide version, so they can still parse the latest C#.
- `renovate.json` no longer updates `Microsoft.CodeAnalysis.*` in `src/Synapse.Generator/`.
- A generator built against a newer Roslyn on purpose would ship one analyzer per Roslyn version
  (`analyzers/dotnet/roslyn4.8/cs`, `analyzers/dotnet/roslyn5.12/cs`); the SDK loads the newest one its compiler
  accepts. Not needed today.

**Verification.** `GeneratorRoslynFloorTests.Generator_ReferencedRoslynAssembly_IsNoNewerThanTheOldestSupportedSdk`
fails on 3.4.0 (`5.12.0.0`) and passes with the fix. A consumer on .NET SDK 10.0.404 builds with the packed
generator.
