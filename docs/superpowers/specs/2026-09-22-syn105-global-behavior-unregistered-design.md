# SYN105 — global behavior declared but its RegisterGroup never registered — Design

Issue: #91.

## Goal

The docs half of #91 already shipped (`docs/docs/modular-monolith.mdx`, `docs/docs/pipelines.mdx#sharing-behaviors-across-projects`): the multi-project topology (shared behaviors library + module handler assemblies + host as composition root) is documented, including the requirement that the host register its own generated group.

What remains is the DX gap the issue's later comments narrowed to: neither `ValidateSynapse()` (#102) nor the SYN101–104 analyzers (#103/#106) can detect the case where `[assembly: SynapseGlobalBehavior(typeof(Behavior<,>))]` is declared but the host never calls `cfg.AddRegisterGroup(new Host.RegisterGroup())` for its own generated group — the attribute is accepted, nothing runs, no error anywhere. This adds a fifth analyzer rule, SYN105, to catch it at build time.

## Rule

Lives alongside SYN101–104 in `src/Synapse.Generator/Analyzers`, same packaging (shipped in the generator's NuGet under `analyzers/dotnet/cs`), same default: **Warning**, category `Synapse.Analyzers`, tunable via `.editorconfig` (`dotnet_diagnostic.SYN105.severity = none`).

| ID | Fires when | Reported at |
|---|---|---|
| SYN105 | This compilation both carries `[assembly: SynapseGlobalBehavior(...)]` and calls `AddSynapse(...)` somewhere (evidence it is a composition root) — but no `AddRegisterGroup(...)` call anywhere in it supplies this compilation's own generated `RegisterGroup` (custom-named via `[RegisterGroup]`, or the default `<RootNamespace>.RegisterGroup`) | Each `[assembly: SynapseGlobalBehavior(...)]` attribute (one diagnostic per attribute occurrence) |

**Scope decision (the fork resolved during design):** assembly-level attributes are not inherited across references, so `[assembly: SynapseGlobalBehavior]` only ever has effect in the compilation that declares it, and only if that same compilation also acts as the composition root — exactly the shape the docs show (attribute and `AddRegisterGroup` co-located in the Host project). SYN105 requires evidence of an `AddSynapse(...)` call in the same compilation before firing. A pure library that declares the attribute with no Synapse DI wiring code in the same project stays silent — a false negative, consistent with the analyzer suite's stated "prefer false negatives" rule of thumb (`docs/superpowers/specs/2026-09-20-roslyn-analyzers-design.md`'s design section), and with the fact that such a declaration is inert either way (a downstream host can't "inherit" the attribute from a referenced library).

Out of scope (documented limitation, not attempted): a broader cross-assembly rule that would catch a shared library (e.g. `Platform`) declaring `[assembly: SynapseGlobalBehavior]` where *no* downstream host anywhere in the solution ever registers `Platform.RegisterGroup`. A single-compilation analyzer cannot see other compilations' `AddRegisterGroup` calls, and the docs already steer users to declare the attribute at the composition root, where this rule applies directly.

## Design

Same shape as the existing four rules: `[DiagnosticAnalyzer(LanguageNames.CSharp)]`, `RegisterCompilationStartAction`, symbol/syntax actions collecting into per-compilation concurrent sets, report in `CompilationEnd`. `EnableConcurrentExecution()`; `ConfigureGeneratedCodeAnalysis` matches the existing rules' choice per file (SYN101–104 mix `None`/`Analyze` depending on whether they need to see generated code — SYN105 needs `Analyze` since the host's `AddSynapse(...)`/`AddRegisterGroup(...)` calls are themselves user code, not generated, so either setting is safe; use `GeneratedCodeAnalysisFlags.None` to match the majority of the existing rules and the stated principle of never analyzing Synapse's own generated `RegisterGroup`). If the compilation does not reference `UnambitiousFx.Synapse.Abstractions`, return immediately (existing guard, reused via `SynapseSymbols.ReferencesSynapse`).

**Collection, during `RegisterCompilationStartAction`:**
1. **Global behavior attributes**: `compilation.Assembly.GetAttributes()` filtered to `SynapseGlobalBehaviorAttribute` (by `AttributeClass?.ToDisplayString()`, matching the existing `SynapseSymbols` helpers' style) — collect each `AttributeData` plus its declaring syntax reference (for the diagnostic location; assembly attributes' `ApplicationSyntaxReference` gives the `[assembly: ...]` location).
2. **`AddSynapse` call evidence**: a symbol action on `InvocationExpressionSyntax` (or `RegisterOperationAction(OperationKind.Invocation)`, whichever matches the existing rules' idiom in this codebase — check `RequestWithoutHandlerAnalyzer.cs`/`BehaviorWithoutHandlersAnalyzer.cs` for the established pattern before choosing) matching the invoked method's containing type and name against `UnambitiousFx.Synapse.DependencyInjectionExtensions.AddSynapse`. A single match is enough; stop collecting further once found (a `bool`/flag in the concurrent state, not a list).
3. **`AddRegisterGroup` call sites**: the same invocation-action pass also matches calls to `UnambitiousFx.Synapse.ISynapseConfig.AddRegisterGroup` (by method symbol — `MethodSymbol.Name == "AddRegisterGroup"` and containing type's `ToDisplayString() == "UnambitiousFx.Synapse.ISynapseConfig"`, following the interface method resolution idiom already used for other cross-type checks in this codebase). For each match, resolve the argument expression's static type via `SemanticModel.GetTypeInfo(argumentExpression).Type` and record its fully-qualified name.

**`CompilationEnd`:**
- If no global-behavior attributes were collected, or `AddSynapse` was never seen, report nothing.
- Otherwise resolve this compilation's own expected generated-group `(namespace, className)` via the shared naming helper (see below) and check whether any recorded `AddRegisterGroup` argument type's fully-qualified name equals `<namespace>.<className>`. If not, report SYN105 once per collected `[assembly: SynapseGlobalBehavior(...)]` attribute, at that attribute's location.

**Shared naming helper (avoids generator/analyzer drift).** `SynapseGenerator.cs` currently inlines the default-vs-`[RegisterGroup]`-target decision (`src/Synapse.Generator/SynapseGenerator.cs:471-475`, using `registerGroupTarget`/`rootNamespace`). Extract this into one `internal static` helper — e.g. `RegisterGroupNaming.Resolve(string? rootNamespace, RegisterGroupTarget? target) -> (string Namespace, string ClassName)` in a new `src/Synapse.Generator/RegisterGroupNaming.cs` — and have `SynapseGenerator.cs` call it instead of its inline tuple expression. The analyzer computes its own `RegisterGroupTarget?` equivalent (see below) and calls the same helper, so the default-name and custom-name rules can never diverge between the two.

**Analyzer's own `[RegisterGroup]` detection.** The generator finds a `[RegisterGroup]`-attributed class via an incremental `SyntaxProvider.ForAttributeWithMetadataName` step (`GetRegisterGroupTarget`, `SynapseGenerator.cs:483+`). The analyzer, running via ordinary compilation/symbol actions rather than the incremental generator pipeline, finds the same thing via a `RegisterSymbolAction(SymbolKind.NamedType)` that checks each named type's attributes for `RegisterGroupAttribute` (by display-string match, same idiom as `SynapseSymbols.IsHandlerAttribute`) and, if found, captures `(type.ContainingNamespace.ToDisplayString(), type.Name)`. This does not need the generator's fuller validation (partial/non-generic/non-nested/at-most-one checks) — SYN105 only needs the name a *valid* `[RegisterGroup]` class would resolve to; if the class is invalid in a way the generator itself would already diagnose (MDG-prefixed diagnostics), SYN105 can still just use whatever name is declared, since a genuinely broken `[RegisterGroup]` class means the generator emits nothing at all under `rootNamespace`+`"RegisterGroup"` either — out of scope for SYN105 to also detect (already covered by the generator's own existing diagnostics).

**Root namespace resolution for the analyzer.** `context.Options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue("build_property.RootNamespace", ...)` (available on `AnalysisContext`/`CompilationStartAnalysisContext.Options`, same property the generator reads), falling back to the existing `CompilationExtensions.GetRootNamespaceFromAssemblyAttributes()` extension (already `internal`, in the same project, reusable as-is).

## Testing

New `test/Synapse.Generator.Tests/Analyzers/GlobalBehaviorRegisterGroupAnalyzerTests.cs`, using the existing hand-rolled `AnalyzerTestHelper` (`RunAsync<TAnalyzer>`, `RunAtPathAsync`, `RunWithReferenceAsync`). Cases:
- `[assembly: SynapseGlobalBehavior]` + `AddSynapse(...)` + `AddRegisterGroup(new RegisterGroup())` (default name) → no diagnostic.
- `[assembly: SynapseGlobalBehavior]` + `AddSynapse(...)` + `AddRegisterGroup` called for *other* groups only (simulating the ModuleA/ModuleB-registered-but-Host-forgotten shape) → SYN105.
- `[assembly: SynapseGlobalBehavior]` + `AddSynapse(...)` + no `AddRegisterGroup` call at all → SYN105.
- `[assembly: SynapseGlobalBehavior]` with **no** `AddSynapse(...)` call anywhere → no diagnostic (the library case).
- No `[assembly: SynapseGlobalBehavior]` at all → no diagnostic, regardless of `AddRegisterGroup` calls.
- Custom `[RegisterGroup]`-named class (e.g. `MyNamespace.MyGroup`) registered correctly → no diagnostic; registered under the *wrong* (default-guessed) name → SYN105.
- Multiple `[assembly: SynapseGlobalBehavior]` attributes with the group correctly registered → one attribute, zero diagnostics (not per-attribute-firing when the fix is already correct).
- Generated code (the `RegisterGroup.g.cs` file itself) is not analyzed — mirrors the existing rules' test coverage for this.

If the hand-rolled `AnalyzerTestHelper`'s `CSharpCompilation` construction doesn't currently thread through `build_property.RootNamespace` (it likely doesn't — it's not an MSBuild-driven compile), tests exercise the `GetRootNamespaceFromAssemblyAttributes()` fallback path (assembly name) rather than the `build_property.RootNamespace` path; this is noted as a plan-time detail, not a design gap, since both paths funnel into the same shared naming helper and existing generator tests already work this way.

## Docs

- `docs/docs/source-generator.mdx`: add the SYN105 row to the existing SYN1xx diagnostics table (cause, fix, `.editorconfig` opt-out), matching SYN101–104's format.
- `docs/docs/modular-monolith.mdx`: the existing sentence "If `Host.RegisterGroup` is not registered, the `SynapseGlobalBehavior` attributes have no effect and no error is raised" gets a short addendum pointing at SYN105 as the build-time catch for this, once it ships.
- `docs/docs/pipelines.mdx#sharing-behaviors-across-projects`: same short addendum where the silent no-op is already called out.
- No known-issue entry (feature, not a bug fix).

## Out of scope

- The broader cross-assembly reachability version of this check (a library's `[assembly: SynapseGlobalBehavior]` whose group is never registered by *any* downstream host across the whole solution) — not feasible for a single-compilation analyzer, and the docs already steer around it.
- Any change to `ValidateSynapse()`/`ValidateOnStart()` runtime checks — this is a build-time-only gap; the runtime has nothing to inspect (the generator emits nothing when the attribute goes unused), as already noted in this issue's own comment thread.
- Changing `SynapseGenerator.cs`'s actual emission behavior — only extracting its existing naming decision into a shared, reusable helper.
