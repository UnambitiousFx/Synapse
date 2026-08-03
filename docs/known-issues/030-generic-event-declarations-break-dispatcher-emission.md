# [Bug]: Generic event / handler declarations emit uncompilable dispatcher registrations

**Severity:** Medium
**Area:** Generator
**Discovered on:** `main`, .NET 10, while auditing `docs/known-issues/` against the v2 refactor
**Status:** ✅ **Resolved** on `main` — see [Resolution](#resolution).

> **TL;DR.** A generic `IEvent` or `IEventHandler<T>` **declaration** was collected for dispatcher
> registration, so `RegisterGroup.g.cs` emitted `typeof(global::Ns.Changed<T>)` — a type parameter that
> is not in scope at the registration site — and the generated file did not compile.

---

## Describe the bug

`ExtractEventInfo` walks every named type in the compilation and records the ones implementing `IEvent`
or `IEventHandler<T>`. It never excluded generic *definitions*, and it captured names with
`ITypeSymbol.ToDisplayString()` — no `global::` prefix. `EventDispatcherRegistrationFactory` then
re-globalized those strings by hand:

```csharp
// src/Synapse.Generator/EventDispatcherRegistrationFactory.cs — before
if (input.Contains("<"))
{
    var genericType = input.Substring(0, input.IndexOf("<", StringComparison.Ordinal));
    var underlyingType = input.Substring(input.IndexOf("<", StringComparison.Ordinal) + 1,
        input.IndexOf(">", StringComparison.Ordinal) - input.IndexOf("<", StringComparison.Ordinal) - 1);
    return $"global::{genericType}<global::{underlyingType}>";
}
```

For `Ns.Changed<T>` that produced `global::Ns.Changed<global::T>` — CS0246, the type parameter is not a
type. Declaring one generic event was enough to break the whole generated registration group for the
assembly.

The same parser is the string-munging class of bug that issue
[017](017-splittoplevelargs-ignores-tuple-parentheses.md) removed from `RegisterGroupFactory`; it
survived here because dispatcher emission is a separate path. It also split on the *first* `<` and `>`,
so a nested generic or tuple argument would have been truncated — unreachable in practice, since only
declarations reach this collector and a closed construction never appears as one.

---

## Steps to reproduce

1. Declare a generic event and its handler in a project that uses the generator:
   `public sealed record Changed<T>(T Value) : IEvent;`
2. Build.

---

## Expected behavior

The generated registration group compiles. A generic definition has no closed runtime type to dispatch
on, so it is simply not registered — such events fall back to runtime dispatch.

---

## Actual behavior

`RegisterGroup.g.cs` failed to compile:
`error CS0246: The type or namespace name 'T' could not be found`, once per emission site
(`register(typeof(…))` plus each `[DynamicDependency]`).

---

## Code sample

```csharp
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace TestNs;

public sealed record Changed<T>(T Value) : IEvent;

public sealed class ChangedHandler<T> : IEventHandler<Changed<T>>
{
    public ValueTask<Result> HandleAsync(Changed<T> @event, CancellationToken ct = default)
        => ValueTask.FromResult(Result.Success());
}
```

---

## Library version

`main` (pre-release, v2 development)

## .NET version

.NET 10.0

## Operating system

macOS

---

## Additional context

### Root cause

Two independent gaps compounded: the collector did not distinguish a generic definition from a
concrete type, and the emitter tried to recover a fully-qualified name from a display string instead of
being handed one.

### Resolution

- `EventInfoSymbolVisitor` skips types with type parameters for both the event and the handler set
  (`src/Synapse.Generator/SynapseGenerator.cs`).
- It records names via `ToEmitName` (`SymbolDisplayFormat.FullyQualifiedFormat`), the same helper 017
  introduced, so `EventInfo` holds emission-ready names.
- `EventDispatcherRegistrationFactory.GlobalizeType` was deleted; the names are interpolated verbatim.
  This removes the last hand-rolled globalizer from the generator.

**Verification.** `test/Synapse.Generator.Tests/GeneratorBehaviorTests.cs` —
`RegisterDispatchers_WithGenericEventDeclaration_SkipsItAndStillCompiles` declares a generic event and
generic handler alongside a concrete pair, asserts the concrete event is still registered
(`register(typeof(global::TestNs.UserCreated)`), that the generic definition is absent, and runs the
output back through the compiler via `AssertGeneratedCompiles`.
