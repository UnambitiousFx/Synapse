# [Bug]: An event nested in a generic type breaks dispatcher emission

**Severity:** Medium
**Area:** Generator
**Discovered on:** `main`, .NET 10, code review of the fix for issue
[030](030-generic-event-declarations-break-dispatcher-emission.md)
**Status:** ✅ **Resolved** on `main` — see [Resolution](#resolution).

> **TL;DR.** Issue 030's guard tested the event's own arity, so a **non-generic** event nested inside a
> generic type slipped through and still emitted `typeof(global::N.Box<T>.Opened)` — the same `CS0246` as the
> original bug.

---

## Describe the bug

Issue [030](030-generic-event-declarations-break-dispatcher-emission.md) fixed generic event *declarations*
being collected for dispatcher registration by skipping them:

```csharp
// src/Synapse.Generator/SynapseGenerator.cs — before
var isGenericDefinition = symbol.TypeParameters.Length > 0;
```

`TypeParameters` reports only the type's **own** arity. A non-generic type nested inside a generic one
declares none, yet its fully-qualified name still contains the outer type's parameter, because that is what
the name is:

```
global::TestNs.Box<T>.Opened
```

The visitor reaches such a type: `VisitNamedType` recurses into `GetTypeMembers()` unconditionally, even for
enclosing types it just skipped. So the registration emitted `typeof(global::TestNs.Box<T>.Opened)`, where
`T` is not in scope at the registration site, and `RegisterGroup.g.cs` failed to compile — exactly the
symptom 030 reported.

The same blind spot existed in `ContainsTypeParameter`, which the request/stream handler collector uses:
it walked type *arguments* but not the containing-type chain, so a handler for a nested-in-generic request
type had the same escape.

030's regression test used a top-level generic event, so it passed either way.

---

## Steps to reproduce

1. Declare an event nested inside a generic type:
   ```csharp
   public class Box<T>
   {
       public sealed record Opened : IEvent;
   }
   ```
2. Build a project that runs the Synapse generator.

---

## Expected behavior

The type is skipped — its name cannot be written at the registration site, so it falls back to runtime
dispatch — and the generated registration compiles.

---

## Actual behavior

`RegisterGroup.g.cs` contained `register(typeof(global::TestNs.Box<T>.Opened), …)` and the build failed with
`CS0246: The type or namespace name 'T' could not be found`.

---

## Code sample

```csharp
namespace TestNs;

public class Box<T>
{
    public sealed record Opened : IEvent;                    // declares no type parameters of its own

    public sealed class OpenedHandler : IEventHandler<Opened>
    {
        public ValueTask<Result> HandleAsync(Opened @event, CancellationToken ct = default)
            => ValueTask.FromResult(Result.Success());
    }
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

Asking a symbol for its own arity rather than asking whether its *emitted name* can be written where it will
be written. A type parameter reaches the name through two channels — the type's own type arguments and its
enclosing types — and the guard inspected only the first.

### Resolution

`ContainsTypeParameter` now walks the containing-type chain as well as the type arguments, and the visitor
uses it instead of an arity check:

```csharp
case INamedTypeSymbol named:
    foreach (var typeArgument in named.TypeArguments) { … }

    // A type nested in a generic one carries its outer type parameters in its emitted name even
    // when it declares none of its own.
    return named.ContainingType is not null && ContainsTypeParameter(named.ContainingType);
```

Because the visitor applies the same flag to events and handlers, and the handler collector already routed
through `ContainsTypeParameter`, one change closes every path.

**Verification.** `test/Synapse.Generator.Tests/GeneratorBehaviorTests.cs` —
`RegisterDispatchers_WithEventNestedInAGenericType_SkipsItAndStillCompiles` declares an event and handler
inside `Box<T>` alongside a concrete pair, asserts the concrete event is still registered, and compiles the
generated output. Reverting the guard to the arity check makes it fail with
`Assert.DoesNotContain() … Found: "Box<"` on the emitted `typeof(global::TestNs.Box<T>.Opened)`.
