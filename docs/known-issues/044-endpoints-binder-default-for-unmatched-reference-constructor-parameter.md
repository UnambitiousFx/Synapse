# [Bug]: Endpoints binder emits bare `default` for an unmatched reference-typed constructor parameter, raising a nullable-reference warning in generated code

**Severity:** Medium
**Area:** Generator (`src/Synapse.Endpoints.Generator`)
**Discovered on:** `feat/synapse-endpoints`, .NET 9/10, `Nullable=enable`
**Status:** ✅ **Resolved** on `feat/synapse-endpoints` — see [Resolution](#resolution).

> **TL;DR.** When a bodyless-verb bound type is constructed through its primary constructor and a
> constructor parameter has no matching bindable property, the binder emitter now emits `default!`
> for a reference-typed parameter (bare `default` is unchanged for a value-typed one), suppressing the
> nullable-reference warning that bare `default` raised under `#nullable enable`.

---

## Describe the bug

`Synapse.Endpoints.Generator`'s `BinderEmitter.EmitPrimaryConstructorCall` builds a `new T(...)` call
for a bound type that has no parameterless constructor (a positional record, or any hand-written type
whose only accessible constructor takes parameters). Each constructor parameter is matched by name to
a resolved bindable property; a parameter with no matching property at all — not merely one omitted
for having no `TryParse` — was emitted as the bare literal `default`.

For a value-typed parameter this is unremarkable. For a **reference-typed** parameter, `default` under
`#nullable enable` is `null`, and assigning it to a non-nullable reference-typed constructor parameter
raises a nullable-reference warning (CS8625-class: "Cannot convert null literal to non-nullable
reference type") in the generated `SynapseEndpointBinders.g.cs`.

The project's own test harness, `GeneratorHarness.AssertGeneratedCompiles`, only inspects
`updated.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)` — by design, since
generator-authored code legitimately triggers some analyzer warnings this repo doesn't want to
treat as generator bugs — so this specific warning passed every existing test silently. A consumer
application that builds with `TreatWarningsAsErrors`/`WarningsAsErrors` (as this repository's own
`build.props` does for itself) would fail to build the moment its bound message type had this shape,
even though `Synapse.Endpoints.Generator`'s own test suite reported all green.

---

## Steps to reproduce

1. Declare a bodyless-verb (`GET`/`DELETE`/`HEAD`) endpoint whose bound message type has no
   parameterless constructor and whose sole/primary constructor takes a reference-typed parameter
   (e.g. `string`) that does not correspond to any bindable property by name.
2. Generate and compile with `#nullable enable` and `TreatWarningsAsErrors=true`.

```csharp
public sealed class HandWrittenQuery : IRequest<int>
{
    public HandWrittenQuery(string label) { }
    public int Id { get; init; }
}

[Get("/handwritten")]
public sealed class HandWrittenEndpoint : Endpoint<HandWrittenQuery, int>;
```

---

## Expected behavior

The generated `new global::TestNs.HandWrittenQuery(...)` call compiles without a nullable-reference
warning, regardless of the consumer's warning configuration.

---

## Actual behavior

The generator emitted `new global::TestNs.HandWrittenQuery(default)`, which is valid C# but raises a
nullable-reference warning on the `label` argument under `#nullable enable`. `AssertGeneratedCompiles`
never caught this because it only fails the test on Error-severity diagnostics.

---

## Code sample

```csharp
// Before (Task 15): always bare `default`, regardless of the parameter's type.
argumentExpressions.Add("default");

// After (Task 17): null-forgiving for reference types, unchanged for value types.
argumentExpressions.Add(parameter.IsReferenceType ? "default!" : "default");
```

---

## Library version

`feat/synapse-endpoints` (pre-release; `Synapse.Endpoints` / `Synapse.Endpoints.Generator` not yet
published)

## .NET version

.NET 9.0, .NET 10.0 (generator project targets `netstandard2.0`)

## Operating system

macOS (Darwin), reproducible on any platform running the Roslyn source generator

---

## Additional context

### Root cause

`ResolveConstructionStrategy` (`EndpointsGenerator.cs`) previously tracked only constructor parameter
*names* (`EquatableArray<string>`), discarding whether each parameter's type was a reference or value
type. `BinderEmitter.EmitPrimaryConstructorCall` therefore had no information available to distinguish
the two cases when emitting a default for an unmatched parameter, and always chose the value-type-safe
literal `default`.

### Resolution

Introduced `Model/ConstructorParameterModel` (name + `IsReferenceType`, computed from
`IParameterSymbol.Type.IsReferenceType` at analysis time) and threaded it through
`EndpointTarget.PrimaryConstructorParameters` and `BoundTypeInfo.PrimaryConstructorParameters` in
place of the bare name array. `BinderEmitter.EmitPrimaryConstructorCall` now emits `default!` for a
reference-typed unmatched parameter and bare `default` for a value-typed one.

**Verification.** Added
`BinderEmissionEdgeCaseTests.Generate_ForPositionalConstructorParameterWithNoMatchingProperty_UsesNullForgivingDefaultForReferenceType`
in `test/Synapse.Endpoints.Generator.Tests/BinderEmissionEdgeCaseTests.cs`, asserting on the emitted
text directly (`Assert.Contains("new global::TestNs.HandWrittenQuery(default!)", generated)`) since a
compile check alone cannot catch a regression back to bare `default` — that still compiles cleanly, it
only warns. `dotnet build Synapse.slnx` succeeds with zero warnings, and the full generator test suite
(63 tests) and the `Synapse.Endpoints.Tests` runtime suite (116 tests) pass.
