# [Bug]: An XML cref with no matching `using` fails the package build

**Severity:** High
**Area:** AspNetCore mapping
**Discovered on:** `feat/synapse-endpoints`, .NET 8/9/10, macOS (arm64)
**Status:** ✅ **Resolved** on `feat/synapse-endpoints` — see [Resolution](#resolution).

> **TL;DR.** `Endpoint.cs` documented `<see cref="HttpContext" />` without importing
> `Microsoft.AspNetCore.Http`; with `TreatWarningsAsErrors` on, that CS1574 failed the build of
> `UnambitiousFx.Synapse.Endpoints` on every target framework. The `using` is now present.

---

## Describe the bug

`src/Synapse.Endpoints/Endpoint.cs` carries only `using UnambitiousFx.Synapse.Abstractions;`, but
the XML documentation on `Endpoint<TRequest, TResponse>` refers to `<see cref="HttpContext" />`. A
cref resolves against the file's own using directives, so the name could not be bound.

On its own that is CS1574, a warning. Two repo-wide settings turn it into a build failure:

- `build.props` sets `TreatWarningsAsErrors` (and `WarningsAsErrors`) to `true`.
- `src/Directory.Build.props` sets `GenerateDocumentationFile` to `true`, so the cref is checked
  at all.

The result is that the shipped project could not be built — or packed — directly, on any of its
three target frameworks.

---

## Steps to reproduce

1. Check out `feat/synapse-endpoints` at `b821115` (or any commit from `96cfcf0` onward).
2. Run `dotnet build src/Synapse.Endpoints/Synapse.Endpoints.csproj`.

---

## Expected behavior

The project builds. It is the package project: `dotnet pack` has to be able to run against it.

---

## Actual behavior

Three errors, one per target framework, and no output assembly:

```
src/Synapse.Endpoints/Endpoint.cs(49,76): error CS1574: XML comment has cref attribute
  'HttpContext' that could not be resolved [.../Synapse.Endpoints.csproj::TargetFramework=net10.0]
src/Synapse.Endpoints/Endpoint.cs(49,76): error CS1574: ... TargetFramework=net9.0]
src/Synapse.Endpoints/Endpoint.cs(49,76): error CS1574: ... TargetFramework=net8.0]

    0 Warning(s)
    3 Error(s)
```

---

## Code sample

```csharp
// src/Synapse.Endpoints/Endpoint.cs — the file imports Synapse.Abstractions and nothing else
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Endpoints;

/// <remarks>
///     <para>
///         Endpoints are stateless singletons: one instance is created at startup, <c>Configure</c>
///         runs once, and the same instance serves every request. Constructor injection is therefore
///         unavailable by design — take what you need from the <see cref="HttpContext" /> passed to
///         <c>OnSuccess</c>.
///         <!--                                          ^^^^^^^^^^^ CS1574: not imported here -->
///     </para>
/// </remarks>
public abstract class Endpoint<TRequest, TResponse> : BoundEndpoint<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : notnull;
```

---

## Library version

`feat/synapse-endpoints` (unreleased)

## .NET version

.NET 8.0, 9.0 and 10.0 — all three fail identically.

## Operating system

macOS (arm64). Nothing about it is platform-specific.

---

## Additional context

### Root cause

`HttpContext` is used in this file only from a documentation comment, never from code, so the
missing import produced no ordinary compile error to notice. Every other file in the project that
documents `HttpContext` — `RawEndpoint.cs`, `BoundEndpoint.cs`, `InlineEndpoint.cs`,
`IEndpointPreProcessor.cs`, `Binding/HttpContextBindingExtensions.cs` — imports
`Microsoft.AspNetCore.Http` because it also *uses* the type. `Endpoint<…>` is the one tier whose
body is empty: it is a marker with nothing but documentation, so it is the one file where a
documentation-only reference had no import to ride along with.

### Resolution

Added the import:

```csharp
using Microsoft.AspNetCore.Http;
using UnambitiousFx.Synapse.Abstractions;
```

**Verification.** `dotnet build src/Synapse.Endpoints/Synapse.Endpoints.csproj` now reports
`0 Warning(s)  0 Error(s)` across net8.0, net9.0 and net10.0. That the failure was not introduced
by the change it was found alongside was confirmed by stashing that change and rebuilding: the same
three CS1574 errors appeared with a pristine tree at `HEAD`. All nine test projects pass afterwards
(`Synapse.Endpoints.Tests` 233, `Synapse.Endpoints.Generator.Tests` 251,
`Synapse.Endpoints.Testing.Tests` 55, `Synapse.Endpoints.OpenApi.Tests` 16, `EndpointsApi.Tests` 60,
`Synapse.Tests` 285, `Synapse.AspNetCore.Tests` 29, `Synapse.Generator.Tests` 62,
`MinimalApi.Tests` 9).
