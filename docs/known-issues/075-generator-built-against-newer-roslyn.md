# [Bug]: The 3.4.0 generator fails every build on an SDK older than .NET 11 (CS9057)

**Severity:** **High**
**Area:** Source Generator
**Discovered on:** `v3.4.0`, consumer building with .NET SDK 10.0.404
**Status:** ✅ **Resolved** on `fix/generator-roslyn-floor` — see [Resolution](#resolution).

> **TL;DR.** `UnambitiousFx.Synapse.Generator` was built against the Roslyn of the newest SDK, so an older
> compiler refused to load it. The package now ships one generator per target framework (.NET 8, 9, 10, 11),
> each built against the Roslyn of that .NET's first SDK, and the consumer's SDK loads the newest one its
> compiler accepts.

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
other Microsoft package, and the package shipped a single build in `analyzers/dotnet/cs`. The consumer's compiler
loads it, and that compiler comes from the SDK: a single build is either pinned to the oldest supported SDK or
breaks every older one.

### Resolution

- The package ships one generator per target framework of the library, each built against the Roslyn of that
  .NET's first SDK, in `analyzers/dotnet/roslynX.Y/cs`:

  | TFM | First SDK | Roslyn | Folder |
  |---|---|---|---|
  | `net8.0` | 8.0.100 | 4.8 | `roslyn4.8` |
  | `net9.0` | 9.0.100 | 4.12 | `roslyn4.12` |
  | `net10.0` | 10.0.100 | 5.0 | `roslyn5.0` |
  | `net11.0` | 11.0.100 | 5.12 | `roslyn5.12` |

  The SDK loads the newest folder its compiler accepts (SDK 10.0.404, Roslyn 5.9, loads `roslyn5.0`), so a
  newer SDK gets a generator built against a newer Roslyn instead of being held to the oldest one.
- `Synapse.Generator.csproj` lists these versions (`SupportedRoslyn`) and, at pack time, restores and builds the
  project once per version into `obj/roslynX.Y` and `bin/roslynX.Y`. The default build, used by the tests and
  the examples, is the newest one (5.12). A new TFM in `LibraryTargetFrameworks` needs a new line there.
- The Roslyn versions are written in the project, out of Renovate's reach.

**Verification.** Each DLL of the packed generator references the `Microsoft.CodeAnalysis` of its folder. A
`net8.0` consumer builds with the packed generator on .NET SDK 8.0.428, 9.0.321, 10.0.404 and 11.0.100; the
compiler receives `roslyn4.8`, `roslyn4.12`, `roslyn5.0` and `roslyn5.12` respectively (it fails with CS9057
on 3.4.0 with every SDK but 11).
