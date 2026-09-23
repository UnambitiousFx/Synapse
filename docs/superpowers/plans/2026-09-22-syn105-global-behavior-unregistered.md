# SYN105 Global Behavior Unregistered Analyzer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add SYN105, a Roslyn analyzer that catches `[assembly: SynapseGlobalBehavior(...)]` declared without the compilation's own generated `RegisterGroup` ever being passed to `AddRegisterGroup` — the silent no-op issue #91's DX half asks for (the docs half already shipped).

**Architecture:** Extract the generator's default-vs-custom `RegisterGroup` naming decision into one shared helper (Task 1, pure refactor, no behavior change), add the new analyzer alongside SYN101–104 using it (Task 2), then update the three doc pages that already reference this exact gap (Task 3).

**Tech Stack:** C# / Roslyn (`Microsoft.CodeAnalysis.CSharp` 5.9.0, netstandard2.0), xUnit v3 (Microsoft Testing Platform), hand-rolled `CSharpCompilation` analyzer test harness (no analyzer-testing package available on the feed).

**Spec:** `docs/superpowers/specs/2026-09-22-syn105-global-behavior-unregistered-design.md`

## Global Constraints

- `TreatWarningsAsErrors=true` repo-wide — every build must stay warning-free.
- `src/Synapse.Generator` targets `netstandard2.0`; the new analyzer file and the shared helper must compile there (no C# language features or BCL APIs unavailable on netstandard2.0 — everything used below already appears elsewhere in this project).
- SYN105 defaults to **Warning**, category `Synapse.Analyzers`, tunable via `.editorconfig` (`dotnet_diagnostic.SYN105.severity = none`), matching SYN101–104 exactly.
- **Scope (the design's resolved fork):** SYN105 fires only when the compilation both declares `[assembly: SynapseGlobalBehavior(...)]` **and** calls `AddSynapse(...)` somewhere in the same compilation. A library with the attribute and no `AddSynapse(...)` call in the same project stays silent — a deliberate false negative, matching the analyzer suite's stated "prefer false negatives" rule.
- Tests: AAA-with-comments style, `Method_Scenario_ExpectedBehavior` naming, matching the existing four analyzers' test files in `test/Synapse.Generator.Tests/Analyzers/`.
- Run tests with `dotnet test --project <csproj> -f <tfm> --filter-class "*ClassName"` — **not** `--filter`, which silently runs zero tests under Microsoft Testing Platform. `test/Synapse.Generator.Tests` only targets `net9.0` (overridden in its own `.csproj`).
- Central package management (`Directory.Packages.props`) — no new packages are needed for this plan; if a task discovers one is genuinely required, add its `<PackageVersion>` there, never a `Version=` attribute on a `<PackageReference>`.

---

### Task 1: Extract `RegisterGroupNaming`, a shared helper for the generator's default-vs-custom group name

**Files:**
- Create: `src/Synapse.Generator/RegisterGroupNaming.cs`
- Modify: `src/Synapse.Generator/SynapseGenerator.cs:471-475` (the `(emitNamespace, emitClassName, emitAsPartial)` tuple assignment, inside the block right before `ctx.AddSource("RegisterGroup.g.cs", ...)`)
- Test: `test/Synapse.Generator.Tests/RegisterGroupNamingTests.cs`

**Interfaces:**
- Consumes: nothing new — `RegisterGroupTarget` (`src/Synapse.Generator/RegisterGroupTarget.cs`, already exists) supplies `Namespace`/`ClassName` for the custom-target case.
- Produces: `internal static class RegisterGroupNaming { public static (string Namespace, string ClassName) Resolve(string rootNamespace, (string Namespace, string ClassName)? customTarget); }` — Task 2's analyzer calls this exact method with the same signature to compute the expected group name it looks for in `AddRegisterGroup` calls.

- [ ] **Step 1: Write the failing tests**

Create `test/Synapse.Generator.Tests/RegisterGroupNamingTests.cs`:

```csharp
using UnambitiousFx.Synapse.Generator;

namespace UnambitiousFx.Synapse.Generator.Tests;

public sealed class RegisterGroupNamingTests
{
    [Fact]
    public void Resolve_WithNoCustomTarget_ReturnsDefaultNameInRootNamespace()
    {
        // Arrange (Given)
        // Act (When)
        var (@namespace, className) = RegisterGroupNaming.Resolve("MyApp.Host", null);

        // Assert (Then)
        Assert.Equal("MyApp.Host", @namespace);
        Assert.Equal("RegisterGroup", className);
    }

    [Fact]
    public void Resolve_WithCustomTarget_ReturnsTheCustomNameIgnoringRootNamespace()
    {
        // Arrange (Given)
        // Act (When)
        var (@namespace, className) = RegisterGroupNaming.Resolve("MyApp.Host",
            ("MyApp.Host.Wiring", "MyGroup"));

        // Assert (Then)
        Assert.Equal("MyApp.Host.Wiring", @namespace);
        Assert.Equal("MyGroup", className);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --project test/Synapse.Generator.Tests/Synapse.Generator.Tests.csproj -f net9.0 --filter-class "*RegisterGroupNamingTests"`

Expected: build error — `RegisterGroupNaming` does not exist yet.

- [ ] **Step 3: Create the shared helper**

`src/Synapse.Generator/RegisterGroupNaming.cs`:

```csharp
namespace UnambitiousFx.Synapse.Generator;

/// <summary>
///     Resolves the namespace and class name a compilation's generated <c>RegisterGroup</c> is emitted under:
///     the class marked <c>[RegisterGroup]</c> when one was declared and validated, otherwise the default
///     <c>RegisterGroup</c> in the assembly's root namespace. Shared by <see cref="SynapseGenerator" /> (which
///     emits under this name) and the <c>SYN105</c> analyzer (which needs to know what name to look for in an
///     <c>AddRegisterGroup</c> call), so the two can never disagree.
/// </summary>
internal static class RegisterGroupNaming
{
    /// <summary>
    ///     Resolves the emitted namespace and class name.
    /// </summary>
    /// <param name="rootNamespace">
    ///     The assembly's root namespace, used only when <paramref name="customTarget" /> is <see langword="null" />.
    /// </param>
    /// <param name="customTarget">
    ///     The namespace and class name of a valid <c>[RegisterGroup]</c>-attributed class, or <see langword="null" />
    ///     when none was declared (or the one declared was invalid — callers resolve validity before calling this).
    /// </param>
    public static (string Namespace, string ClassName) Resolve(string rootNamespace,
        (string Namespace, string ClassName)? customTarget)
    {
        return customTarget is { } target
            ? (target.Namespace, target.ClassName)
            : (rootNamespace, "RegisterGroup");
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --project test/Synapse.Generator.Tests/Synapse.Generator.Tests.csproj -f net9.0 --filter-class "*RegisterGroupNamingTests"`

Expected: PASS, both tests.

- [ ] **Step 5: Wire the generator to use the helper (pure refactor, no behavior change)**

In `src/Synapse.Generator/SynapseGenerator.cs`, find:

```csharp
            var (emitNamespace, emitClassName, emitAsPartial) = registerGroupTarget is { } target
                ? (target.Namespace, target.ClassName, true)
                : (rootNamespace, "RegisterGroup", false);
```

Replace with:

```csharp
            var (emitNamespace, emitClassName) = RegisterGroupNaming.Resolve(rootNamespace,
                registerGroupTarget is { } target ? (target.Namespace, target.ClassName) : null);
            var emitAsPartial = registerGroupTarget is not null;
```

This is behavior-identical: `emitNamespace`/`emitClassName` resolve exactly as before, `emitAsPartial` is still `true` exactly when a valid `[RegisterGroup]` target exists.

- [ ] **Step 6: Run the full existing generator test suite to confirm zero regressions**

Run: `dotnet test --project test/Synapse.Generator.Tests/Synapse.Generator.Tests.csproj -f net9.0`

Expected: PASS, same total count as before this change (this refactor must not change any existing test's outcome — if anything fails, the refactor introduced a behavior difference; fix `RegisterGroupNaming`/the call site to match the original tuple expression exactly, don't weaken a test).

- [ ] **Step 7: Build the whole solution**

Run: `dotnet build Synapse.slnx`

Expected: 0 errors, 0 warnings.

- [ ] **Step 8: Commit**

```bash
git add src/Synapse.Generator/RegisterGroupNaming.cs src/Synapse.Generator/SynapseGenerator.cs test/Synapse.Generator.Tests/RegisterGroupNamingTests.cs
git commit -m "refactor(generator): extract RegisterGroupNaming, shared with the upcoming SYN105 analyzer (#91)"
```

---

### Task 2: `GlobalBehaviorRegisterGroupAnalyzer` (SYN105)

**Files:**
- Create: `src/Synapse.Generator/Analyzers/GlobalBehaviorRegisterGroupAnalyzer.cs`
- Modify: `src/Synapse.Generator/AnalyzerReleases.Unshipped.md`
- Modify: `test/Synapse.Generator.Tests/Synapse.Generator.Tests.csproj` (add a `ProjectReference` to `src/Synapse/Synapse.csproj`)
- Modify: `test/Synapse.Generator.Tests/Analyzers/AnalyzerTestHelper.cs` (add two metadata references)
- Test: `test/Synapse.Generator.Tests/Analyzers/GlobalBehaviorRegisterGroupAnalyzerTests.cs`

**Interfaces:**
- Consumes: `RegisterGroupNaming.Resolve` (Task 1); `SynapseSymbols.ReferencesSynapse`, `.IsAttribute`, `.IsGenerated` (`src/Synapse.Generator/Analyzers/SynapseSymbols.cs`, pre-existing); `CompilationExtensions.GetRootNamespaceFromAssemblyAttributes` (`src/Synapse.Generator/CompilationExtensions.cs`, pre-existing, `internal`).
- Produces: `public sealed class GlobalBehaviorRegisterGroupAnalyzer : DiagnosticAnalyzer` with `public const string DiagnosticId = "SYN105"` — Task 3's docs reference this ID and its exact fix message.

- [ ] **Step 1: Add the test-only project reference and metadata references**

In `test/Synapse.Generator.Tests/Synapse.Generator.Tests.csproj`, add to the existing `ProjectReference` `ItemGroup`:

```xml
        <ProjectReference Include="..\..\src\Synapse\Synapse.csproj" />
```

(`Synapse.csproj` does not reference `Synapse.Generator`, so this creates no circular project reference. It multi-targets `net8.0;net9.0;net10.0`; the test project's own `net9.0` override resolves against `Synapse.csproj`'s `net9.0` output automatically.)

In `test/Synapse.Generator.Tests/Analyzers/AnalyzerTestHelper.cs`, add a `using` and two reference lines. Add to the `using` block at the top:

```csharp
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Synapse;
```

In `GetMetadataReferences()`, add after the existing two `references.Add(...)` lines:

```csharp
        references.Add(MetadataReference.CreateFromFile(typeof(ISynapseConfig).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(IServiceCollection).Assembly.Location));
```

- [ ] **Step 2: Run the existing analyzer test suite to confirm the project/reference changes don't break anything**

Run: `dotnet test --project test/Synapse.Generator.Tests/Synapse.Generator.Tests.csproj -f net9.0 --filter-class "*RequestWithoutHandlerAnalyzerTests"` (or any one existing analyzer test class — pick whichever file exists in `test/Synapse.Generator.Tests/Analyzers/`)

Expected: PASS — the new references are additive and must not change any existing analyzer test's behavior.

- [ ] **Step 3: Write the failing tests**

Create `test/Synapse.Generator.Tests/Analyzers/GlobalBehaviorRegisterGroupAnalyzerTests.cs`. Every fixture
is passed as separate files via `AnalyzerTestHelper.RunFilesAsync`, never string-concatenated into one file
— C# allows at most one file-scoped `namespace X;` per file and requires `using` directives to precede all
type declarations in their scope, so building a multi-namespace fixture by concatenating raw-string
fragments is a real (and easy to miss) compile error. Keeping each file single-purpose avoids it entirely.

```csharp
using UnambitiousFx.Synapse.Generator.Analyzers;

namespace UnambitiousFx.Synapse.Generator.Tests.Analyzers;

public sealed class GlobalBehaviorRegisterGroupAnalyzerTests
{
    // SomeBehavior (the [assembly: SynapseGlobalBehavior] target) and the default-named RegisterGroup class,
    // both in the default (TestAssembly) namespace — reused by every test that doesn't need a custom name.
    private const string GroupsFile = """
        using UnambitiousFx.Synapse.Abstractions;

        namespace TestAssembly
        {
            public sealed class SomeBehavior<TRequest, TResponse> { }

            public sealed class RegisterGroup : IRegisterGroup
            {
                public void Register(IDependencyInjectionBuilder builder) { }
            }
        }
        """;

    [Fact]
    public async Task Analyze_GlobalBehaviorWithOwnGroupRegistered_ReportsNoDiagnostic()
    {
        // Arrange (Given) — the correctly-wired case: AddSynapse is called, and the compilation's own
        // default-named group (TestAssembly.RegisterGroup, since no build_property.RootNamespace is
        // available in this hand-rolled harness and the assembly is named "TestAssembly") is registered.
        const string wiringFile = """
            using Microsoft.Extensions.DependencyInjection;
            using UnambitiousFx.Synapse;

            [assembly: SynapseGlobalBehavior(typeof(TestAssembly.SomeBehavior<,>))]

            namespace TestAssembly
            {
                public static class Wiring
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSynapse(cfg =>
                        {
                            cfg.AddRegisterGroup(new RegisterGroup());
                        });
                    }
                }
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunFilesAsync<GlobalBehaviorRegisterGroupAnalyzer>(
            ("Groups.cs", GroupsFile), ("Wiring.cs", wiringFile));

        // Assert (Then)
        Assert.Empty(diagnostics.Where(d => d.Id == GlobalBehaviorRegisterGroupAnalyzer.DiagnosticId));
    }

    [Fact]
    public async Task Analyze_OtherGroupsRegisteredButNotOwnGroup_ReportsSyn105()
    {
        // Arrange (Given) — the exact shape from issue #91: another group (simulating ModuleA/ModuleB) is
        // registered, but this assembly's own group never is.
        const string otherGroupFile = """
            using UnambitiousFx.Synapse.Abstractions;

            namespace TestAssembly
            {
                public sealed class OtherGroup : IRegisterGroup
                {
                    public void Register(IDependencyInjectionBuilder builder) { }
                }
            }
            """;
        const string wiringFile = """
            using Microsoft.Extensions.DependencyInjection;
            using UnambitiousFx.Synapse;

            [assembly: SynapseGlobalBehavior(typeof(TestAssembly.SomeBehavior<,>))]

            namespace TestAssembly
            {
                public static class Wiring
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSynapse(cfg =>
                        {
                            cfg.AddRegisterGroup(new OtherGroup());
                        });
                    }
                }
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunFilesAsync<GlobalBehaviorRegisterGroupAnalyzer>(
            ("Groups.cs", GroupsFile), ("OtherGroup.cs", otherGroupFile), ("Wiring.cs", wiringFile));

        // Assert (Then)
        var syn105 = Assert.Single(diagnostics, d => d.Id == GlobalBehaviorRegisterGroupAnalyzer.DiagnosticId);
        Assert.Contains("SynapseGlobalBehavior", syn105.GetMessage());
    }

    [Fact]
    public async Task Analyze_NoAddRegisterGroupCallAtAll_ReportsSyn105()
    {
        // Arrange (Given) — AddSynapse is called (composition root), but no AddRegisterGroup call exists.
        const string wiringFile = """
            using Microsoft.Extensions.DependencyInjection;
            using UnambitiousFx.Synapse;

            [assembly: SynapseGlobalBehavior(typeof(TestAssembly.SomeBehavior<,>))]

            namespace TestAssembly
            {
                public static class Wiring
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSynapse(cfg => { });
                    }
                }
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunFilesAsync<GlobalBehaviorRegisterGroupAnalyzer>(
            ("Groups.cs", GroupsFile), ("Wiring.cs", wiringFile));

        // Assert (Then)
        Assert.Single(diagnostics, d => d.Id == GlobalBehaviorRegisterGroupAnalyzer.DiagnosticId);
    }

    [Fact]
    public async Task Analyze_NoAddSynapseCallAnywhere_ReportsNoDiagnostic()
    {
        // Arrange (Given) — the "shared library, not a host" case: the attribute is declared, but this
        // compilation never calls AddSynapse at all, so it is not acting as a composition root. Silent by
        // design (see the spec's resolved scope fork).
        const string attributeOnlyFile = """
            using UnambitiousFx.Synapse.Abstractions;

            [assembly: SynapseGlobalBehavior(typeof(TestAssembly.SomeBehavior<,>))]
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunFilesAsync<GlobalBehaviorRegisterGroupAnalyzer>(
            ("Groups.cs", GroupsFile), ("Attribute.cs", attributeOnlyFile));

        // Assert (Then)
        Assert.Empty(diagnostics.Where(d => d.Id == GlobalBehaviorRegisterGroupAnalyzer.DiagnosticId));
    }

    [Fact]
    public async Task Analyze_NoGlobalBehaviorAttribute_ReportsNoDiagnosticRegardlessOfRegistration()
    {
        // Arrange (Given) — no [assembly: SynapseGlobalBehavior] anywhere; nothing for SYN105 to check.
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using UnambitiousFx.Synapse;

            namespace TestAssembly
            {
                public static class Wiring
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSynapse(cfg => { });
                    }
                }
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<GlobalBehaviorRegisterGroupAnalyzer>(source);

        // Assert (Then)
        Assert.Empty(diagnostics.Where(d => d.Id == GlobalBehaviorRegisterGroupAnalyzer.DiagnosticId));
    }

    [Fact]
    public async Task Analyze_CustomRegisterGroupNameRegisteredCorrectly_ReportsNoDiagnostic()
    {
        // Arrange (Given) — a [RegisterGroup]-attributed class with a non-default name/namespace, correctly
        // registered under that same name.
        const string customGroupsFile = """
            using UnambitiousFx.Synapse.Abstractions;

            namespace TestAssembly
            {
                public sealed class SomeBehavior<TRequest, TResponse> { }
            }

            namespace TestAssembly.Wiring
            {
                [RegisterGroup]
                public sealed partial class MyGroup : IRegisterGroup
                {
                    public void Register(IDependencyInjectionBuilder builder) { }
                }
            }
            """;
        const string wiringFile = """
            using Microsoft.Extensions.DependencyInjection;
            using UnambitiousFx.Synapse;

            [assembly: SynapseGlobalBehavior(typeof(TestAssembly.SomeBehavior<,>))]

            namespace TestAssembly.Wiring
            {
                public static class Startup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSynapse(cfg =>
                        {
                            cfg.AddRegisterGroup(new MyGroup());
                        });
                    }
                }
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunFilesAsync<GlobalBehaviorRegisterGroupAnalyzer>(
            ("Groups.cs", customGroupsFile), ("Wiring.cs", wiringFile));

        // Assert (Then)
        Assert.Empty(diagnostics.Where(d => d.Id == GlobalBehaviorRegisterGroupAnalyzer.DiagnosticId));
    }

    [Fact]
    public async Task Analyze_CustomRegisterGroupNameRegisteredUnderDefaultGuessedName_ReportsSyn105()
    {
        // Arrange (Given) — a [RegisterGroup]-attributed class (MyGroup) exists, but the code registers a
        // plain "RegisterGroup" instead — the default name SYN105 must NOT fall back to once a valid custom
        // target exists.
        const string customGroupsFile = """
            using UnambitiousFx.Synapse.Abstractions;

            namespace TestAssembly
            {
                public sealed class SomeBehavior<TRequest, TResponse> { }

                public sealed class RegisterGroup : IRegisterGroup
                {
                    public void Register(IDependencyInjectionBuilder builder) { }
                }
            }

            namespace TestAssembly.Wiring
            {
                [RegisterGroup]
                public sealed partial class MyGroup : IRegisterGroup
                {
                    public void Register(IDependencyInjectionBuilder builder) { }
                }
            }
            """;
        const string wrongWiringFile = """
            using Microsoft.Extensions.DependencyInjection;
            using UnambitiousFx.Synapse;

            [assembly: SynapseGlobalBehavior(typeof(TestAssembly.SomeBehavior<,>))]

            namespace TestAssembly
            {
                public static class Startup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSynapse(cfg =>
                        {
                            cfg.AddRegisterGroup(new RegisterGroup());
                        });
                    }
                }
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunFilesAsync<GlobalBehaviorRegisterGroupAnalyzer>(
            ("Groups.cs", customGroupsFile), ("Wiring.cs", wrongWiringFile));

        // Assert (Then)
        Assert.Single(diagnostics, d => d.Id == GlobalBehaviorRegisterGroupAnalyzer.DiagnosticId);
    }

    [Fact]
    public async Task Analyze_MultipleGlobalBehaviorAttributesCorrectlyRegistered_ReportsNoDiagnostics()
    {
        // Arrange (Given) — two [assembly: SynapseGlobalBehavior] entries, group correctly registered:
        // zero diagnostics, not one skipped and one reported.
        const string wiringFile = """
            using Microsoft.Extensions.DependencyInjection;
            using UnambitiousFx.Synapse;

            [assembly: SynapseGlobalBehavior(typeof(TestAssembly.SomeBehavior<,>))]
            [assembly: SynapseGlobalBehavior(typeof(TestAssembly.SomeBehavior<,>))]

            namespace TestAssembly
            {
                public static class Wiring
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSynapse(cfg =>
                        {
                            cfg.AddRegisterGroup(new RegisterGroup());
                        });
                    }
                }
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunFilesAsync<GlobalBehaviorRegisterGroupAnalyzer>(
            ("Groups.cs", GroupsFile), ("Wiring.cs", wiringFile));

        // Assert (Then)
        Assert.Empty(diagnostics.Where(d => d.Id == GlobalBehaviorRegisterGroupAnalyzer.DiagnosticId));
    }

    [Fact]
    public async Task Analyze_AttributeInGeneratedFile_IsNotReported()
    {
        // Arrange (Given) — mirrors the existing rules' coverage: an [assembly: SynapseGlobalBehavior] that
        // sits in a file the analyzer treats as generated (by filename convention) is never reported, even
        // though its group is genuinely unregistered.
        const string wiringFile = """
            using Microsoft.Extensions.DependencyInjection;
            using UnambitiousFx.Synapse;

            [assembly: SynapseGlobalBehavior(typeof(TestAssembly.SomeBehavior<,>))]

            namespace TestAssembly
            {
                public static class Wiring
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSynapse(cfg => { });
                    }
                }
            }
            """;

        // Act (When) — the ".g.cs" suffix on the wiring file's path is what marks it generated.
        var diagnostics = await AnalyzerTestHelper.RunFilesAsync<GlobalBehaviorRegisterGroupAnalyzer>(
            ("Groups.cs", GroupsFile), ("Wiring.g.cs", wiringFile));

        // Assert (Then)
        Assert.Empty(diagnostics.Where(d => d.Id == GlobalBehaviorRegisterGroupAnalyzer.DiagnosticId));
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test --project test/Synapse.Generator.Tests/Synapse.Generator.Tests.csproj -f net9.0 --filter-class "*GlobalBehaviorRegisterGroupAnalyzerTests"`

Expected: build error — `GlobalBehaviorRegisterGroupAnalyzer` does not exist yet.

- [ ] **Step 5: Implement `GlobalBehaviorRegisterGroupAnalyzer`**

`src/Synapse.Generator/Analyzers/GlobalBehaviorRegisterGroupAnalyzer.cs`:

```csharp
using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace UnambitiousFx.Synapse.Generator.Analyzers;

/// <summary>
///     SYN105: <c>[assembly: SynapseGlobalBehavior]</c> is declared and this compilation calls
///     <c>AddSynapse(...)</c> somewhere (evidence it is a composition root), but no
///     <c>AddRegisterGroup(...)</c> call registers this compilation's own generated group — the default
///     <c>RegisterGroup</c> in the assembly's root namespace, or the class marked <c>[RegisterGroup]</c>.
///     The attribute is silently inert in that case: assembly attributes are not inherited across
///     references, so a downstream host cannot pick this up either. A compilation that declares the
///     attribute with no <c>AddSynapse(...)</c> call of its own (a shared library, not a host) is not
///     reported — a deliberate false negative, since the attribute is inert there regardless and this
///     analyzer only sees one compilation at a time.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class GlobalBehaviorRegisterGroupAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "SYN105";

    private const string DependencyInjectionExtensionsTypeName =
        "UnambitiousFx.Synapse.DependencyInjectionExtensions";

    private const string SynapseConfigInterfaceName = "UnambitiousFx.Synapse.ISynapseConfig";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Global behavior's generated group is never registered",
        "[assembly: SynapseGlobalBehavior] is declared but this assembly's generated RegisterGroup is never " +
        "passed to AddRegisterGroup, so the behavior never runs",
        "Synapse.Analyzers",
        DiagnosticSeverity.Warning,
        true,
        "AddSynapse(...) is called in this compilation, so it is a composition root, but no " +
        "AddRegisterGroup(...) call registers this assembly's own generated group. Call " +
        "cfg.AddRegisterGroup(new <ThisAssembly>.RegisterGroup()) alongside the other groups.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            if (!SynapseSymbols.ReferencesSynapse(start.Compilation))
            {
                return;
            }

            var hasAddSynapseCall = 0;
            var customTargets = new ConcurrentBag<(string Namespace, string ClassName)>();
            var registeredGroupTypes = new ConcurrentBag<ITypeSymbol>();

            start.RegisterSymbolAction(symbolContext =>
            {
                var type = (INamedTypeSymbol)symbolContext.Symbol;
                if (type.GetAttributes()
                    .Any(a => SynapseSymbols.IsAttribute(a.AttributeClass, "RegisterGroupAttribute")))
                {
                    customTargets.Add((type.ContainingNamespace.ToDisplayString(), type.Name));
                }
            }, SymbolKind.NamedType);

            start.RegisterOperationAction(opContext =>
            {
                var invocation = (IInvocationOperation)opContext.Operation;
                var method = invocation.TargetMethod;

                if (IsMethod(method, DependencyInjectionExtensionsTypeName, "AddSynapse"))
                {
                    Interlocked.Exchange(ref hasAddSynapseCall, 1);
                    return;
                }

                if (IsMethod(method, SynapseConfigInterfaceName, "AddRegisterGroup"))
                {
                    var argumentType = invocation.Arguments.Length > 0
                        ? invocation.Arguments[0].Value.Type
                        : null;
                    if (argumentType is not null)
                    {
                        registeredGroupTypes.Add(argumentType);
                    }
                }
            }, OperationKind.Invocation);

            start.RegisterCompilationEndAction(endContext =>
            {
                if (Volatile.Read(ref hasAddSynapseCall) == 0)
                {
                    return;
                }

                var globalEntryLocations = new List<Location>();
                foreach (var attribute in endContext.Compilation.Assembly.GetAttributes())
                {
                    if (!SynapseSymbols.IsAttribute(attribute.AttributeClass, "SynapseGlobalBehaviorAttribute"))
                    {
                        continue;
                    }

                    var syntax = attribute.ApplicationSyntaxReference?.GetSyntax(endContext.CancellationToken);
                    if (syntax is null || SynapseSymbols.IsGenerated(syntax.SyntaxTree, endContext.CancellationToken))
                    {
                        continue;
                    }

                    globalEntryLocations.Add(syntax.GetLocation());
                }

                if (globalEntryLocations.Count == 0)
                {
                    return;
                }

                var rootNamespace = ResolveRootNamespace(start.Options, endContext.Compilation);
                var customTarget = customTargets.IsEmpty ? ((string, string)?)null : customTargets.First();
                var (expectedNamespace, expectedClassName) =
                    RegisterGroupNaming.Resolve(rootNamespace, customTarget);
                var expectedFullName = string.IsNullOrEmpty(expectedNamespace)
                    ? expectedClassName
                    : $"{expectedNamespace}.{expectedClassName}";

                var expectedType = endContext.Compilation.GetTypeByMetadataName(expectedFullName);
                if (expectedType is null)
                {
                    // Cannot resolve the expected type in this compilation view — prefer a false negative
                    // over guessing.
                    return;
                }

                var isRegistered = registeredGroupTypes.Any(t =>
                    SymbolEqualityComparer.Default.Equals(t, expectedType));
                if (isRegistered)
                {
                    return;
                }

                foreach (var location in globalEntryLocations)
                {
                    endContext.ReportDiagnostic(Diagnostic.Create(Rule, location));
                }
            });
        });
    }

    /// <summary>
    ///     Whether <paramref name="method" /> is the method named <paramref name="methodName" /> declared on
    ///     <paramref name="containingTypeName" />. Handles both an ordinary static call
    ///     (<c>DependencyInjectionExtensions.AddSynapse(services, cfg)</c>) and an extension-method call
    ///     (<c>services.AddSynapse(cfg)</c>) — for the latter, <see cref="IMethodSymbol.ContainingType" /> on
    ///     the reduced symbol already resolves to the declaring static class in Roslyn's API, but this checks
    ///     <see cref="IMethodSymbol.ReducedFrom" /> too so the match holds even if that changes.
    /// </summary>
    private static bool IsMethod(IMethodSymbol method, string containingTypeName, string methodName)
    {
        if (method.Name != methodName)
        {
            return false;
        }

        var original = method.ReducedFrom ?? method;
        return original.ContainingType.ToDisplayString() == containingTypeName;
    }

    private static string ResolveRootNamespace(AnalyzerOptions options, Compilation compilation)
    {
        if (options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue("build_property.RootNamespace",
                out var rootNamespace) && !string.IsNullOrWhiteSpace(rootNamespace))
        {
            return rootNamespace;
        }

        return compilation.GetRootNamespaceFromAssemblyAttributes();
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test --project test/Synapse.Generator.Tests/Synapse.Generator.Tests.csproj -f net9.0 --filter-class "*GlobalBehaviorRegisterGroupAnalyzerTests"`

Expected: PASS, all 9 cases.

- [ ] **Step 7: Add the SYN105 entry to `AnalyzerReleases.Unshipped.md`**

In `src/Synapse.Generator/AnalyzerReleases.Unshipped.md`, append to the `### New Rules` table:

```markdown
SYN105 | Synapse.Analyzers | Warning | GlobalBehaviorRegisterGroupAnalyzer
```

- [ ] **Step 8: Run the full generator test project**

Run: `dotnet test --project test/Synapse.Generator.Tests/Synapse.Generator.Tests.csproj -f net9.0`

Expected: PASS, no regressions in any existing test class.

- [ ] **Step 9: Build the whole solution**

Run: `dotnet build Synapse.slnx`

Expected: 0 errors, 0 warnings. Pay particular attention to `examples/` projects (`examples/Directory.Build.props`
promotes `SYN101`–`SYN104` to `WarningsAsErrors`) — if any example project happens to call `AddSynapse` and
declare `[assembly: SynapseGlobalBehavior]` without registering its own group, SYN105 would now warn there
too, but `examples/Directory.Build.props`'s `WarningsAsErrors` list does **not** currently include SYN105 (it
lists `SYN101;SYN102;SYN103;SYN104` explicitly), so a new SYN105 warning there would not fail the build — only
verify the build stays green, do not add SYN105 to that list as part of this plan.

- [ ] **Step 10: Commit**

```bash
git add src/Synapse.Generator/Analyzers/GlobalBehaviorRegisterGroupAnalyzer.cs src/Synapse.Generator/AnalyzerReleases.Unshipped.md test/Synapse.Generator.Tests/Synapse.Generator.Tests.csproj test/Synapse.Generator.Tests/Analyzers/AnalyzerTestHelper.cs test/Synapse.Generator.Tests/Analyzers/GlobalBehaviorRegisterGroupAnalyzerTests.cs
git commit -m "feat(analyzers): SYN105 for a SynapseGlobalBehavior whose group is never registered (#91)"
```

---

### Task 3: Docs

**Files:**
- Modify: `docs/docs/source-generator.mdx`
- Modify: `docs/docs/modular-monolith.mdx`
- Modify: `docs/docs/pipelines.mdx`

**Interfaces:**
- Consumes: `SYN105`'s diagnostic ID and fix message (Task 2).
- Produces: nothing (terminal task).

- [ ] **Step 1: Update `docs/docs/source-generator.mdx`'s diagnostics section**

Find (around line 173):

```markdown
The same `UnambitiousFx.Synapse.Generator` package also ships Roslyn analyzers. They report registration mistakes at compile time and all four default to `Warning`. `SYN103` is live in the editor. `SYN101`, `SYN102` and `SYN104` are compilation-end analyzers: they need the whole assembly, so they report on a build or a full-solution analysis rather than as you type.
```

Replace with:

```markdown
The same `UnambitiousFx.Synapse.Generator` package also ships Roslyn analyzers. They report registration mistakes at compile time and all five default to `Warning`. `SYN103` is live in the editor. `SYN101`, `SYN102`, `SYN104` and `SYN105` are compilation-end analyzers: they need the whole assembly, so they report on a build or a full-solution analysis rather than as you type.
```

Find the table's last row (SYN104, around line 180):

```markdown
| `SYN104` | A class implements a handler interface but has no handler attribute, in an assembly that uses the attributes elsewhere. | Add the attribute, or register the handler manually and silence the rule for that class with `#pragma warning disable SYN104`. |
```

Add a new row immediately after it:

```markdown
| `SYN105` | `[assembly: SynapseGlobalBehavior(...)]` is declared and this assembly calls `AddSynapse(...)` (so it is a composition root), but no `AddRegisterGroup(...)` call registers this assembly's own generated group — the attribute is accepted and the behavior never runs, with no error anywhere else. An assembly that declares the attribute with no `AddSynapse(...)` call of its own (a shared library, not a host) is not reported: the attribute is inert there regardless, since assembly attributes are not inherited by referencing assemblies. | Register this assembly's own generated group: `cfg.AddRegisterGroup(new <ThisAssembly>.RegisterGroup())` (or your `[RegisterGroup]` partial's name), alongside the other groups. |
```

Find the section's closing paragraph (at the very end of the Diagnostics section):

```markdown
The analyzers work at compile time and complement the runtime [`ValidateSynapse()`](./pipelines#validating-the-configuration) check. Neither detects a host that never registers its generated group with `AddRegisterGroup` (tracked in #91): in that case no handler is registered and no diagnostic is reported.
```

Replace with:

```markdown
The analyzers work at compile time and complement the runtime [`ValidateSynapse()`](./pipelines#validating-the-configuration) check. `SYN105` catches the `[assembly: SynapseGlobalBehavior]` case of this (issue #91): a plain handler group that is never registered at all is a different, louder failure — it fails at the first dispatch attempt instead of staying silent, so no analyzer is needed for it.
```

- [ ] **Step 2: Update `docs/docs/modular-monolith.mdx`**

Find (line 64):

```markdown
If `Host.RegisterGroup` is not registered, the `SynapseGlobalBehavior` attributes have no effect and no error is raised. See [Sharing behaviors across projects](./pipelines#sharing-behaviors-across-projects).
```

Replace with:

```markdown
If `Host.RegisterGroup` is not registered, the `SynapseGlobalBehavior` attributes have no effect and no error is raised — `SYN105` catches this at build time once the host also calls `AddSynapse(...)`; see [Diagnostics](./source-generator#diagnostics) and [Sharing behaviors across projects](./pipelines#sharing-behaviors-across-projects).
```

- [ ] **Step 3: Update `docs/docs/pipelines.mdx`**

Find (line 142):

```markdown
`[assembly: SynapseGlobalBehavior]` is emitted into the host assembly's generated `RegisterGroup` (or your `[RegisterGroup]` partial class). If that group is never passed to `AddRegisterGroup`, the attributes are accepted, **nothing fails, and the behaviors never run** ([`ValidateSynapse`](#validating-the-configuration) cannot detect this either). This matters most for security behaviors such as authorization. Add a test that dispatches one request from each module and asserts that the expected behaviors ran.
```

Replace with:

```markdown
`[assembly: SynapseGlobalBehavior]` is emitted into the host assembly's generated `RegisterGroup` (or your `[RegisterGroup]` partial class). If that group is never passed to `AddRegisterGroup`, the attributes are accepted, **nothing fails, and the behaviors never run** ([`ValidateSynapse`](#validating-the-configuration) cannot detect this either — the [`SYN105`](./source-generator#diagnostics) analyzer catches it at build time instead). This matters most for security behaviors such as authorization. Add a test that dispatches one request from each module and asserts that the expected behaviors ran.
```

Find (line 332, in the "What it cannot catch" list):

```markdown
- `[assembly: SynapseGlobalBehavior]` when the host's generated group is never registered (`cfg.AddRegisterGroup(new Host.RegisterGroup())`). The behaviors never reach the container, so the validator sees a valid configuration. A build-time analyzer is tracked in [#103](https://github.com/UnambitiousFx/Synapse/issues/103).
```

Replace with:

```markdown
- `[assembly: SynapseGlobalBehavior]` when the host's generated group is never registered (`cfg.AddRegisterGroup(new Host.RegisterGroup())`). The behaviors never reach the container, so the validator sees a valid configuration. The build-time [`SYN105`](./source-generator#diagnostics) analyzer catches this instead.
```

- [ ] **Step 4: Verify the docs build**

Run: `cd docs && pnpm build`

Expected: succeeds, zero broken-link warnings.

- [ ] **Step 5: Commit**

```bash
git add docs/docs/source-generator.mdx docs/docs/modular-monolith.mdx docs/docs/pipelines.mdx
git commit -m "docs: point the SynapseGlobalBehavior silent-no-op warnings at SYN105 (#91)"
```

---

## After all tasks: final review and merge

Per `superpowers:subagent-driven-development`: dispatch the final whole-branch code review (most
capable available model), address findings with one fix round + scoped re-review, then follow
`superpowers:finishing-a-development-branch` — push, open a PR against `main`, wait for CI green on
all TFMs, squash-merge, close issue #91 with a summary comment.
