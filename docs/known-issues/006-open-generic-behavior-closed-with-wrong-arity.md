# [Bug]: Open-generic behavior closed with wrong type-argument arity

**Severity:** High
**Area:** `Synapse.Generator`
**Discovered on:** `feature/typed-pipeline-behaviors`, .NET 10
**Status:** ✅ **Resolved** on `feature/typed-pipeline-behaviors` — see [Resolution](#resolution).

---

## Describe the bug

When the generator closes an open-generic pipeline behavior, it chooses the number of type
arguments from the **pipeline interface** arity (one for `IRequestPipelineBehavior<TRequest>` /
`IEventPipelineBehavior`, two for `IRequestPipelineBehavior<TRequest, TResponse>` /
`IStreamRequestPipelineBehavior`). The behavior **class's own generic arity is never recorded** —
only a boolean `isOpenGeneric`. When the class has more type parameters than the interface it
implements, the generated code closes it with the wrong number of type arguments and fails to
compile.

---

## Steps to reproduce

1. Declare a behavior whose class arity exceeds its interface arity:

   ```csharp
   [PipelineBehavior]
   public sealed class LogBehavior<TRequest, TState>
       : IRequestPipelineBehavior<TRequest>
       where TRequest : IRequest
   {
       // ...
   }
   ```

2. Build a project that has a matching request handler.

---

## Expected behavior

Either the generator closes all of the class's type parameters correctly, or it reports a clear
diagnostic that the behavior's extra type parameters cannot be inferred.

---

## Actual behavior

The generator emits `LogBehavior<SomeCommand>` (one type argument) for a class that requires two,
producing **CS0305** ("using the generic type `LogBehavior<TRequest, TState>` requires 2 type
arguments").

---

## Root cause

- The class's own arity is captured only as `isOpenGeneric = classSymbol.IsGenericType`
  (`src/Synapse.Generator/SynapseGenerator.cs:415`) — a bool, not a count.
- Closing arity at emit time is driven solely by the interface `Kind`
  (`src/Synapse.Generator/RegisterGroupFactory.cs:162`, `:175`, `:185`, `:198`): one argument for
  `Request`/`Event`, two for `RequestWithResponse`/`StreamRequest`.
- The request/response type names come from `iface.TypeArguments[i].Name`, never from the class's
  own type-parameter list.

---

## To address

- Record the behavior class's type-parameter count (and how each maps to the interface's type
  arguments). If a class parameter is not bound by the interface, it cannot be inferred — emit a
  diagnostic rather than malformed code.
- Add a generator test for a behavior whose class arity differs from its interface arity.

## Resolution

The behavior class's own type-parameter list is now recorded and used to drive closing, instead of
inferring arity from the interface `Kind`.

**1. A per-parameter closing map.** `BehaviorDetail` gained
`EquatableArray<int> ClosingTypeArgumentMap` — one entry per class type parameter (in
declaration order), holding the index of the interface type argument that binds it (`0` = request /
event slot, `1` = response / item slot) or `-1` when no interface type argument references the
parameter. `SynapseGenerator.BuildClosingTypeArgumentMap` builds it by walking
`iface.TypeArguments` and recording each `ITypeParameterSymbol`'s `Ordinal`. Closed behaviors get an
empty map.

**2. Closing in class order/arity.** `RegisterGroupFactory.CloseBehavior` emits the closed type by
substituting each class parameter from the map (`req` for `0`, `resp` for `1`) in class-declaration
order — so both extra-arity and reordered-parameter classes close correctly. It falls back to the old
interface-arity behavior when the map is empty.

**3. Uninferable parameters fail loud.** When the map contains a `-1`
(`BehaviorDetail.HasUnbindableTypeParameter`), the generator reports new diagnostic **MDG010**
(`Error`) and skips the behavior, instead of emitting code that fails with CS0305.

**Verification.** Covered by `test/Synapse.Generator.Tests/GeneratorBehaviorTests.cs`:
`OpenGenericBehavior_WithExtraUnbindableTypeParameter_EmitsMDG010AndNoRegistration` and
`OpenGenericBehavior_WithReorderedTypeParameters_ClosesInClassOrder`; existing open-generic tests stay
green. Full solution builds clean.

## Library version

`feature/typed-pipeline-behaviors` (pre-release)

## .NET version

.NET 10.0
