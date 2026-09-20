# [Bug]: Generator ignores generic constraints on the behavior's own type parameters

**Severity:** Medium
**Area:** Generator
**Discovered on:** `main` (2.0.1), .NET 10, found while implementing the `ICommand` / `IQuery` markers (issue #94)
**Status:** ✅ **Resolved** on `feat/cqrs-markers` — see [Resolution](#resolution).

> **TL;DR.** The source generator skipped every constraint that references the behavior's own type parameters, so
> `where TRequest : ICommand<TResponse>` did not scope a `[PipelineBehavior]` and the behavior was closed over every
> handler, producing CS0311. Such constraints are now matched by their unbound generic definition against the handler
> request's (and response's) own type, generic interfaces and generic base types.

---

## Describe the bug

`GetConstraintNames` / `GetSatisfyingTypeNames` in `SynapseGenerator` decide which handlers an open-generic behavior
(`[PipelineBehavior]` or `[assembly: SynapseGlobalBehavior]`) is closed over. Constraints whose type references another
type parameter of the behavior, for example `where TRequest : ICommand<TResponse>` or
`where TRequest : RequestBase<TResponse>`, were ignored entirely. The behavior was therefore closed over every handler,
including those whose request does not satisfy the constraint, and the generated registration did not compile.

---

## Steps to reproduce

1. Declare `public record CreateTaskCommand(string Title) : ICommand<Guid>;` and `public record GetTaskQuery(Guid Id) : IQuery<TaskDto>;` with handlers for both.
2. Declare a `[PipelineBehavior]` `TransactionBehavior<TRequest, TResponse>` constrained with `where TRequest : ICommand<TResponse>`.
3. Build.

---

## Expected behavior

The behavior is closed over `CreateTaskCommand` only.

## Actual behavior

The behavior is also closed over `GetTaskQuery`, and `RegisterGroup.g.cs` fails to compile with CS0311.

---

## Code sample

```csharp
[PipelineBehavior]
public sealed class TransactionBehavior<TRequest, TResponse>
    : IRequestPipelineBehavior<TRequest, TResponse>
    where TRequest : ICommand<TResponse>
    where TResponse : notnull
{
    public ValueTask<Result<TResponse>> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TRequest, TResponse> next,
        CancellationToken cancellationToken = default)
        => next(request, cancellationToken);
}
```

---

## Library version

`main` (2.0.1)

## .NET version

.NET 10.0

## Operating system

macOS

---

## Additional context

### Root cause

The constraint collection only kept constraints that could be resolved without the behavior's type parameters. A
constraint like `ICommand<TResponse>` mentions `TResponse`, so it could not be turned into a concrete type name and was
dropped, which left the cross-product unfiltered.

### Resolution

Constraints that reference the behavior's own type parameters are now recorded by their unbound generic definition
(`ICommand<>`, `RequestBase<>`) and matched against the handler request's own type, its generic interfaces and its
generic base types, and likewise the response type for constraints on `TResponse`. Only handlers that satisfy the
constraint are closed over. Fixed in commits 6943fb7 and 118f523 (the latter keeps base-class and self-generic
constraints when scoping behaviors).

Known limits:

- A constraint is a necessary condition only. Agreement of the response type (for example `ICommand<Guid>` versus a
  `TResponse` of another type) is still left to the compiler, so an over-included handler still yields CS0311 as before.
- The `SYN102` analyzer still ignores constraints on type parameters, so a marker-scoped behavior that matches no
  handler is silently unwired without a `SYN102` warning. Parked as a follow-up.

**Verification.** `test/Synapse.Generator.Tests/CqrsMarkerGeneratorTests.cs` covers behaviors scoped by
`ICommand<TResponse>`, `IQuery<TResponse>` and generic base-class constraints, for both `[PipelineBehavior]` and
`SynapseGlobalBehavior`; the full solution test suite passes.
