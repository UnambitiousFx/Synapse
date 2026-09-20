# Roslyn analyzers for missing handlers and unwired behaviors — Design

Issue: #103 (sub-issue of #96). Complements the runtime checks of #102 (`ValidateSynapse`).

## Goal

Catch at build time what the source generator silently ignores: a request type with no handler, a behavior
that would be emitted for no handler, a handler attribute the generator skips, and a handler class left out of the
attribute-driven registration.

## Packaging

The analyzers live in the existing `src/Synapse.Generator` project and DLL (netstandard2.0, shipped as a development
dependency under `analyzers/dotnet/cs`). No new package. Diagnostic IDs use the prefix `SYN1xx`; the generator keeps
`MDG0xx`; the runtime validator (#102) keeps `SYN0xx`. Category `Synapse.Analyzers`.

## Rules

All default to **Warning**, enabled by default, tunable with `.editorconfig`
(`dotnet_diagnostic.SYN101.severity = none`).

| ID | Fires when | Reported at |
|---|---|---|
| SYN101 | A non-abstract, non-open-generic class, record or struct in this compilation implements `IRequest` or `IRequest<T>` and no type in the compilation implements the matching `IRequestHandler<TRequest>` / `IRequestHandler<TRequest,TResponse>` for it | The request type |
| SYN102 | A `[PipelineBehavior]` class, or an `[assembly: SynapseGlobalBehavior(typeof(B))]` entry, would be emitted for zero handlers | The behavior class / the attribute |
| SYN103 | A handler attribute (`[RequestHandler]`, `[EventHandler]`, `[StreamRequestHandler]`) sits on a `record`, which the generator skips because it only accepts `ClassDeclarationSyntax`. (`struct` and interface are already compiler errors, since the attributes target classes.) | The attribute |
| SYN104 | A class implements `IRequestHandler`, `IEventHandler` or `IStreamRequestHandler`, has no handler attribute, and the assembly has at least one attributed handler | The class |

Out of scope: events for SYN101 (zero subscribers is legal), stream requests for SYN101, the #91 silent no-op
(`[assembly: SynapseGlobalBehavior]` with the host's generated group never registered; not chosen for this slice,
so #91 stays open), and mismatched or abstract handler classes (already compile errors).

## Design

Each rule is one `DiagnosticAnalyzer` (`[DiagnosticAnalyzer(LanguageNames.CSharp)]`) using symbols only:
`RegisterCompilationStartAction`, then symbol actions collecting into per-compilation concurrent sets, then reports in
a `CompilationEnd` action. `EnableConcurrentExecution()` and `ConfigureGeneratedCodeAnalysis(None)`: generated
code (including Synapse's own `RegisterGroup`) is never analyzed. If the compilation does not reference
`UnambitiousFx.Synapse.Abstractions`, every analyzer returns immediately.

Rule of thumb: **prefer false negatives**. When a rule cannot decide, it stays silent.

- **SYN101.** Collect source types implementing `IRequest`/`IRequest<T>` (excluding abstract, open generic,
  interfaces) and source types implementing `IRequestHandler<...>`; report requests whose type is not matched. Only
  this compilation is scanned, so a handler in another assembly is a false positive; documented with the
  `.editorconfig` opt-out.
- **SYN102.** Handlers visible to a behavior are those in this compilation plus those in referenced assemblies that
  themselves reference `Synapse.Abstractions` (cheap filter; no walk of framework assemblies). A closed behavior needs a
  handler for its request type. An open-generic behavior needs at least one visible handler whose request type
  satisfies the interface constraint types; if the behavior has special constraints (`class`, `struct`, `new()`)
  or anything the analyzer cannot evaluate, it does not report. Behavior shapes reuse the same interface detection as
  the generator (`IRequestPipelineBehavior<>`, `IRequestPipelineBehavior<,>`, `IEventPipelineBehavior<>`,
  `IStreamRequestPipelineBehavior<,>`).
- **SYN103.** Syntax-node action on `RecordDeclarationSyntax` with a Synapse handler attribute (resolved by symbol, so
  aliases and `[RequestHandler<,>]` generic forms work).
- **SYN104.** After collecting: if at least one class in the compilation has a handler attribute, every source class
  that implements a handler interface without one is reported. Manual registration (`cfg.RegisterRequestHandler<>()`)
  cannot be seen, so the message says how to silence it.

Shared symbol helpers (interface detection, attribute detection) live in one internal static class in the
generator project and are used by the analyzers; the existing generator code is not refactored beyond reusing constants
if trivial.

## Testing

A hand-rolled helper builds a `CSharpCompilation` (references: runtime, `Synapse.Abstractions`) and runs
`WithAnalyzers`, returning diagnostics; the analyzer testing package is not available on the feed, so no new
package is added (only `Microsoft.CodeAnalysis.CSharp`, already referenced by the generator tests). One test class
per rule, positive and negative cases, including: SYN101 request with handler in compilation, abstract request,
open-generic request, handler for a different request; SYN102 closed and open-generic behaviors, constrained
open-generic, global behavior entry, handler in a referenced assembly; SYN103 record vs class; SYN104 attributed
assembly vs an assembly with no attributed handler; generated code is not analyzed. MinimalApi must build with no
new warnings.

## Docs

A "Diagnostics" section in `docs/docs/source-generator.mdx`: the `SYN1xx` table with cause, fix, and the `.editorconfig`
opt-out; a note that SYN101 only sees the current assembly. No known-issue entry: this is a feature.
