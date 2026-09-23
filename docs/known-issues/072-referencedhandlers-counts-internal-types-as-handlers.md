# [Bug]: SYN102 silently never fires when a referenced assembly's internal generic type happens to satisfy the behavior's constraint

**Severity:** Medium
**Area:** Generator
**Discovered on:** `worktree-syn105-global-behavior`, .NET 9, while implementing the SYN105 analyzer (issue #91)
**Status:** ✅ **Resolved** on `worktree-syn105-global-behavior` — see [Resolution](#resolution).

> **TL;DR.** `BehaviorWithoutHandlersAnalyzer.ReferencedHandlers()` scanned every type in a
> referenced assembly with no accessibility filter, so `UnambitiousFx.Synapse.dll`'s own internal
> `ProxyRequestHandler<,>` registration plumbing was picked up as if it were a real user-written
> handler — silently defeating SYN102 for any open-generic behavior constrained to bare `IRequest`
> (or `IRequest<TResponse>`, `IEvent`, `IStreamRequest<TItem>`). `ReferencedHandlers()` now only
> considers `public` types.

---

## Describe the bug

`BehaviorWithoutHandlersAnalyzer` (SYN102) reports a warning when a `[PipelineBehavior]` class or
an `[assembly: SynapseGlobalBehavior]` entry matches no handler visible from the compilation. Its
`ReferencedHandlers()` helper walks every `INamedTypeSymbol` in an assembly the compilation
references (when that assembly itself references `Synapse.Abstractions`), looking for types that
implement one of the Synapse handler interfaces (`IRequestHandler<,>`, `IEventHandler<>`, etc.), with
no filter on the type's accessibility.

Any consumer that calls `AddSynapse(...)` necessarily references `UnambitiousFx.Synapse.dll`. That
assembly ships its own internal registration plumbing —
`internal sealed class ProxyRequestHandler<TRequestHandler, TRequest> : IRequestHandler<TRequest>
where TRequest : IRequest` (and its two-type-parameter and stream-request-handler siblings) — which
is a perfectly valid symbol match for `SynapseSymbols.GetHandlerInterface`. Because
`ProxyRequestHandler`'s own `TRequest` type parameter is constrained only to `IRequest`, Roslyn's
`ClassifyCommonConversion` between that type parameter and a behavior's bare `IRequest` constraint
classifies as implicit (a type parameter always implicitly converts to its own constraint), so
`MayApply` concludes a matching handler exists — even when the analyzed compilation has genuinely
zero real handlers anywhere.

## Steps to reproduce

1. In a compilation that references `UnambitiousFx.Synapse.dll` (i.e. any project using
   `AddSynapse(...)`), declare an open-generic pipeline behavior with no narrower-than-`IRequest`
   constraint and **no handler at all**:

   ```csharp
   [PipelineBehavior]
   public sealed class LoggingBehavior<TRequest> : IRequestPipelineBehavior<TRequest>
       where TRequest : IRequest
   {
       public ValueTask<Result> HandleAsync(TRequest request, RequestHandlerDelegate<TRequest> next,
           CancellationToken ct = default) => next(request, ct);
   }
   ```

2. Run SYN102 (`BehaviorWithoutHandlersAnalyzer`) over the compilation.

## Expected behavior

SYN102 fires: "Pipeline behavior 'LoggingBehavior' matches no handler visible from this assembly, so
it never runs."

## Actual behavior

No diagnostic is reported. `ReferencedHandlers()` picked up `UnambitiousFx.Synapse.dll`'s internal
`ProxyRequestHandler<TRequestHandler, TRequest>` as a "handler" whose `TRequest` type parameter
(constrained only to `IRequest`) satisfies the behavior's `where TRequest : IRequest` constraint via
`ClassifyCommonConversion`, so `MayApply` returns `true` and the behavior is treated as applied.

## Code sample

```csharp
// src/Synapse/ProxyRequestHandler.cs — internal plumbing, not a real handler:
internal sealed class ProxyRequestHandler<TRequestHandler, TRequest>
    : IRequestHandler<TRequest>, IPipelineInfo
    where TRequestHandler : class, IRequestHandler<TRequest>
    where TRequest : IRequest
{ /* ... */ }

// Any consumer's LoggingBehavior<TRequest> where TRequest : IRequest, with zero real handlers
// anywhere, was silently treated as "applied" because of the type above — SYN102 never fired.
```

## Library version

`worktree-syn105-global-behavior` (pre-release)

## .NET version

.NET 9.0

## Operating system

macOS

---

## Additional context

### Root cause

`BehaviorWithoutHandlersAnalyzer.ReferencedHandlers()`
(`src/Synapse.Generator/Analyzers/BehaviorWithoutHandlersAnalyzer.cs`) enumerated every
`INamedTypeSymbol` reachable from a referenced assembly's global namespace via
`SynapseSymbols.GetTypes(...)`, filtering only on `TypeKind` (class/struct) and `IsAbstract`. It had
no filter on `DeclaredAccessibility`, so `internal` types — implementation detail that a downstream
consumer never wrote and can never see or reference — counted the same as genuinely public,
user-authored handlers.

This was invisible until now because no analyzer test project previously referenced
`src/Synapse/Synapse.csproj` directly; `test/Synapse.Generator.Tests` only referenced
`Synapse.Abstractions` and `Synapse.Generator`. Adding a `ProjectReference` to `Synapse.csproj` (Task 2
of the SYN105 plan, needed so SYN105's fixtures can call `AddSynapse`/`ISynapseConfig`/
`IServiceCollection`) made `UnambitiousFx.Synapse.dll` a referenced assembly for every test in that
project, which surfaced the pre-existing bug as two newly-failing tests
(`BehaviorWithoutHandlersAnalyzerTests.Analyze_WithAnOpenGenericBehaviorAndNoHandlerAtAll_ReportsSyn102OnTheBehavior`
and `...Analyze_WithAGlobalBehaviorEntryAndNoHandler_ReportsSyn102OnTheAttribute`). The bug itself
predates this branch and plausibly affects real consumer projects today: any project calling
`AddSynapse(...)` references `Synapse.dll`, so an open-generic behavior constrained to exactly
`IRequest`/`IRequest<TResponse>`/`IEvent`/`IStreamRequest<TItem>` (no narrower constraint) with
genuinely zero handlers would silently not be flagged by SYN102 in production. It causes no build
failures on its own — a false negative just means no diagnostic is produced, so
`TreatWarningsAsErrors`/`WarningsAsErrors` never catches it — which is why it went unnoticed.

### Resolution

`ReferencedHandlers()` now only considers `public` types:

```csharp
foreach (var type in SynapseSymbols.GetTypes(assembly.GlobalNamespace, cancellationToken))
{
    // Only public types are handlers a consumer could plausibly have declared: an internal type in a
    // referenced assembly (e.g. Synapse's own ProxyRequestHandler<,> plumbing) is implementation detail,
    // never something this analyzer should count as "a real handler this behavior applies to".
    if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct) || type.IsAbstract
        || type.DeclaredAccessibility != Accessibility.Public)
    {
        continue;
    }
    // ...
}
```

A dedicated regression test,
`BehaviorWithoutHandlersAnalyzerTests.Analyze_WithOnlyAnInternalOpenGenericHandlerInAReferencedAssembly_ReportsSyn102`,
reproduces the same shape (an `internal` generic handler over a type parameter constrained only to
`IRequest`) in an isolated referenced assembly via `AnalyzerTestHelper.RunWithReferenceAsync`, so the
regression is pinned independently of `Synapse.dll`'s own internals ever changing, rather than relying
on the incidental fact that the test project now references `Synapse.csproj`.

**Verification.** The two previously-failing tests and the new regression test pass, and are confirmed
to fail for the right reason (not just an empty-collection accident) by reading the returned
diagnostics (`Assert.Equal("SYN102", diagnostic.Id)`, message contains the behavior's name). The full
`test/Synapse.Generator.Tests` project passes 128/128. `dotnet build Synapse.slnx` is 0 warnings,
0 errors.
