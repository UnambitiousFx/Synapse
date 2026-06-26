# [Bug]: `GlobalizeType` emits invalid code for tuple / pointer / function-pointer types

**Severity:** Medium
**Area:** `Synapse.Generator`
**Discovered on:** `feature/typed-pipeline-behaviors`, .NET 10
**Status:** ✅ **Resolved** on `feature/typed-pipeline-behaviors` — see [Resolution](#resolution).

---

## Describe the bug

`GlobalizeSimpleType` (and `GlobalizeType`) prefixes type display strings with `global::` after only
stripping trailing `?`/`[`/`]` and checking against a C# keyword set. Display strings that are not
simple named types — value tuples, pointers, function pointers — receive a malformed `global::`
prefix and produce uncompilable generated code. Arbitrary value-type response types flow through
this path (see [001](001-open-generic-pipeline-behavior-aot-value-type.md)), so a tuple-returning
query can break the generated build.

---

## Steps to reproduce

1. Declare a query whose response is a value tuple:

   ```csharp
   public sealed record GetTotals : IRequest<(int Open, int Done)>;
   ```

2. Provide a matching handler / open-generic behavior so the generator emits a closed registration,
   then build.

---

## Expected behavior

The generated registration references the response type correctly and compiles.

---

## Actual behavior

The generator emits `global::(int, string)` (and similarly `global::delegate*<...>`, `global::int*`),
which fails to compile (e.g. **CS1031**).

---

## Root cause

`src/Synapse.Generator/RegisterGroupFactory.cs:299` (`GlobalizeSimpleType`):

- A tuple `(int, string)` has no `<`, so it reaches `GlobalizeSimpleType`; it is not in the keyword
  set and does not start with `global::`, so it becomes `global::(int, string)`.
- `delegate*<...>` contains `<`, so `GlobalizeType` splits on `<` with base `delegate*` →
  `global::delegate*<...>`.
- `int*` keeps its `*` (not stripped, not a keyword) → `global::int*`.

Response types are rendered via `ToDisplayString()` (`SynapseGenerator.cs:540` / `:549`) before
reaching this code.

---

## To address

- Globalize from the `ITypeSymbol` using
  `ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)` (which handles tuples, pointers, arrays
  and nullability correctly) instead of string-munging a display string.
- If string handling must remain, special-case tuple syntax (recurse into each element) and
  pointer/function-pointer forms.
- Add a generator test with a tuple response type.

## Resolution

Response/item types are now globalized from the `ITypeSymbol` with Roslyn's built-in
`SymbolDisplayFormat.FullyQualifiedFormat` (via the new `ToEmitName` helper in
`src/Synapse.Generator/SynapseGenerator.cs`), which renders tuples, pointers, function pointers,
arrays and nullability correctly. The fragile string-munging in
`RegisterGroupFactory.GlobalizeType` is no longer applied to those pre-globalized strings — the
response/item emission sites in `src/Synapse.Generator/RegisterGroupFactory.cs` interpolate them
verbatim. `GlobalizeType` is retained only for handler / target / request / event / validator-class
names, which are always named types.

Output for named types, keywords, generics, nullables and arrays is byte-identical to before, so
existing registrations are unchanged. Cross-source/cross-assembly dedup is unaffected because every
response string is produced by the same conversion path.

A regression test was added in `test/Synapse.Generator.Tests/GeneratorBehaviorTests.cs`:
`RequestHandler_WithTupleResponse_EmitsCorrectlyGlobalizedRegistration` — a tuple-returning query
with an open-generic behavior, asserting the generated source never contains a malformed `global::(`
prefix.

## Library version

`feature/typed-pipeline-behaviors` (pre-release)

## .NET version

.NET 10.0
