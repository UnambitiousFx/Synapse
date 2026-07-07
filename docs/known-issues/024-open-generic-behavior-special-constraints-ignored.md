# [Bug]: Open-generic behavior special generic constraints (class/struct/unmanaged/notnull/new()) are ignored

**Severity:** High
**Area:** Generator
**Discovered on:** `main`, .NET 9 / .NET 10, while comparing the pipeline design against `martinothamar/Mediator`
**Status:** ✅ **Resolved** on `main` — see [Resolution](#resolution).

> **TL;DR.** The generator's open-generic cross-product only honoured named-type constraints; special
> constraints (`class`/`struct`/`unmanaged`/`notnull`/`new()`) were dropped, so a constrained behavior
> was closed over non-conforming handlers and emitted uncompilable code (CS0453).

---

## Describe the bug

When the source generator closes an open-generic `[PipelineBehavior]` over the handlers it applies to
(`RegisterGroupFactory.EmitOpenGenericBehaviorRegistrations`), it filters candidates with `Satisfies()`,
which checks only the behavior's **named-type** constraints. Those come from `GetConstraintNames`, which
reads `ITypeParameterSymbol.ConstraintTypes`.

The C# *special* constraints — `class`, `struct`, `unmanaged`, `notnull`, `new()` — are **not** constraint
types; they are boolean flags on the type-parameter symbol (`HasReferenceTypeConstraint`,
`HasValueTypeConstraint`, …). They were never captured, so a behavior carrying them recorded *no*
constraints and matched **every** handler.

---

## Steps to reproduce

1. Declare an open-generic behavior with a special constraint, e.g. `where TResponse : struct`.
2. Have at least one handler whose response is a reference type.
3. Build. The generated `RegisterGroup.g.cs` registers the behavior closed over the reference-type
   response, violating the constraint.

---

## Expected behavior

The behavior is registered only for handlers whose request/response satisfy **all** of its constraints,
including special constraints. Non-conforming handlers are skipped. Generated code always compiles.

---

## Actual behavior

The special constraint is ignored; the behavior is closed over every handler. For a reference type bound
to a `struct`-constrained parameter the generated code fails to compile with **CS0453** (the type must be
a non-nullable value type).

---

## Code sample

```csharp
public sealed record IntRequest : IRequest<int>;
public sealed record StringRequest : IRequest<string>;

[RequestHandler<IntRequest, int>]    public sealed class IntHandler    : IRequestHandler<IntRequest, int> { /* … */ }
[RequestHandler<StringRequest, string>] public sealed class StringHandler : IRequestHandler<StringRequest, string> { /* … */ }

[PipelineBehavior]
public sealed class StructCache<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : struct                 // ← dropped → matched StringRequest too
{ /* … */ }

// Generated (before fix), uncompilable:
//   builder.RegisterRequestPipelineBehavior<StructCache<StringRequest, string>, StringRequest, string>();  // CS0453
```

---

## Library version

`main` (branch `feature/typed-pipeline-behaviors`)

## .NET version

.NET 9.0 / .NET 10.0

## Operating system

macOS

---

## Additional context

### Root cause

The generator reduces behaviors and handlers to **equatable string/value records** (`BehaviorDetail`,
`HandlerDetail`) so the incremental generator can cache — Roslyn symbols never cross into the emit stage
where the cross-product runs. The constraint capture (`GetConstraintNames`) only translated
`ConstraintTypes` to strings and had no representation for the special-constraint flags, so they were
silently lost. `Satisfies()` then treated a special-constraint-only behavior as unconstrained.

### Resolution

Captured the missing data as equatable values and enforced it in the emit stage (no symbols added to the
pipeline, so incrementality is preserved — deliberately *not* a Roslyn `ClassifyConversion` port, which
would require live symbols at cross-product time):

- New `[Flags] SpecialConstraints` enum + `RequestSpecialConstraints`/`ResponseSpecialConstraints` on
  `BehaviorDetail`, populated by `GetSpecialConstraints` (reads `HasReferenceTypeConstraint`,
  `HasValueTypeConstraint`, `HasUnmanagedTypeConstraint`, `HasNotNullConstraint`,
  `HasConstructorConstraint`).
- New `TypeShape` record (`IsReferenceType`/`IsValueType`/`IsUnmanaged`/`IsNotNull`/`HasParameterlessCtor`)
  + `RequestShape`/`ResponseShape` on `HandlerDetail`, populated by `GetTypeShape` at both handler
  discovery sites (`GetRequestInfo`, `BehaviorTargetSymbolVisitor.Add`).
- `RegisterGroupFactory.SatisfiesSpecial(...)` evaluates the flags against the handler shape and is added
  to every cross-product case guard alongside the existing named-type `Satisfies(...)`.

**Verification.** Added generator tests covering `struct`/`class`/`new()`/`unmanaged` and a combined
named + special constraint case in `test/Synapse.Generator.Tests/GeneratorBehaviorTests.cs`, each
asserting the correct subset is registered **and** that the generated `RegisterGroup.g.cs` compiles with
no errors (`AssertGeneratedCompiles`). All generator tests (53) and runtime tests (177) pass.
