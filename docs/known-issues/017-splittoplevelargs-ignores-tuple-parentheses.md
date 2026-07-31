# [Bug]: `SplitTopLevelArgs` ignores tuple parentheses, breaking nested-tuple generic arguments

**Severity:** Medium
**Area:** `Synapse.Generator`
**Discovered on:** `feature/typed-pipeline-behaviors`, .NET 10
**Status:** ✅ **Resolved** — request/target/event/behavior/validator types now globalized at the
symbol level via `ToEmitName` (`FullyQualifiedFormat`), and `RegisterGroupFactory`'s string-munging
`GlobalizeType` / `SplitTopLevelArgs` parser was deleted. Covered by
`RequestHandler_WithTupleAsGenericArgument_EmitsCorrectlyGlobalizedRegistration`.

> **Scope note:** this fix covered `RegisterGroupFactory` only. A second copy of the same hand-rolled
> globalizer lived on in `EventDispatcherRegistrationFactory` and was removed by issue
> [030](030-generic-event-declarations-break-dispatcher-emission.md), which is where the dispatcher
> emission path was actually breaking.

---

## Describe the bug

`GlobalizeType` splits a generic type's argument list with `SplitTopLevelArgs`, which tracks nesting
depth using only `<` and `>`. It does **not** track tuple parentheses `(` / `)`. When a generic type
argument is itself a value tuple, the tuple's internal comma is treated as a top-level argument
separator, splitting one argument into two malformed fragments and emitting uncompilable `global::`
code.

This is the residual of [012](012-globalizetype-breaks-on-tuple-and-pointer-types.md): that fix routes
**response/item** types through Roslyn's `FullyQualifiedFormat` (`ToEmitName`) and emits them verbatim,
so a **top-level** tuple response is now safe. But **request / target / event / behavior** type names
still flow through the string-munging `GlobalizeType` path, so a tuple nested as a generic argument of
one of those types is still broken.

---

## Steps to reproduce

1. Declare a request whose type is generic over a value tuple, with a matching handler and an
   open-generic behavior so a closed registration is emitted:

   ```csharp
   public sealed record Query<T>(T Value) : IRequest<int>;
   // handler for Query<(int Id, string Name)>
   ```

2. Build the project so the generator runs.

---

## Expected behavior

The generated registration references `Query<(int Id, string Name)>` correctly and compiles.

---

## Actual behavior

`GlobalizeType("Ns.Query<(int, string)>")` extracts `argsText = "(int, string)"`.
`SplitTopLevelArgs` sees the comma at depth 0 (parentheses are uncounted) and yields `"(int"` and
`" string)"`, producing `global::Ns.Query<global::(int, global:: string)>`, which fails to compile.

---

## Root cause

`src/Synapse.Generator/RegisterGroupFactory.cs` — `SplitTopLevelArgs` (≈ line 367) only increments /
decrements `depth` on `<` and `>`:

```csharp
case '<': depth++; break;
case '>': depth--; break;
case ',' when depth == 0: /* split */ break;
```

`(` and `)` are not handled, so a depth-0 comma inside a tuple is mistaken for an argument separator.

---

## To address

- Prefer globalizing request / target / event / behavior types from their `ITypeSymbol` via
  `ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)` (the same `ToEmitName` path the response
  slot already uses), eliminating the string parser entirely.
- If the string path must remain, count `(`/`)` (and `[`/`]`) in `SplitTopLevelArgs` so commas inside
  tuples/arrays are not treated as top-level.
- Add a generator test with a request type generic over a tuple.

## Library version

`feature/typed-pipeline-behaviors` (pre-release)

## .NET version

.NET 10.0
