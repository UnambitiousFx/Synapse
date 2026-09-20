# Roslyn Analyzers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add four Roslyn diagnostic analyzers (SYN101 request without handler, SYN102 behavior applies to no handler, SYN103 handler attribute on a record, SYN104 unattributed handler in an attribute-using assembly) to the existing `Synapse.Generator` package.

**Architecture:** Four `DiagnosticAnalyzer` classes in `src/Synapse.Generator/Analyzers/`, sharing one internal symbol-helper class. Each collects symbols per compilation (`RegisterCompilationStartAction` + symbol/syntax actions) and reports at compilation end. Rule of thumb: prefer false negatives. Tests use a hand-rolled `CSharpCompilation` + `WithAnalyzers` helper (no analyzer-testing package is available).

**Tech Stack:** Roslyn (`Microsoft.CodeAnalysis.CSharp` 5.9.0, netstandard2.0 analyzer), xUnit v3 on Microsoft Testing Platform (`test/Synapse.Generator.Tests`, net9.0 only).

**Spec:** `docs/superpowers/specs/2026-09-20-roslyn-analyzers-design.md`

## Global Constraints

- The generator project targets `netstandard2.0`, `TreatWarningsAsErrors=true`, `EnforceExtendedAnalyzerRules=true`, `Nullable=enable`, `LangVersion latest`: no APIs missing from netstandard2.0 (no `[NotNullWhen]`, no `init` accessors / positional records without an existing polyfill — use plain classes/structs with constructors), no compiler-warning suppressions to get green (fix the cause; a `RSxxxx` warning must be fixed or, if truly inapplicable, suppressed with a one-line justification and mentioned in your report).
- Analyzer rule metadata: category `Synapse.Analyzers`, default severity `Warning`, enabled by default, IDs `SYN101`–`SYN104`. Every rule is listed in `src/Synapse.Generator/AnalyzerReleases.Unshipped.md` (release tracking, RS2008).
- Every analyzer: `[DiagnosticAnalyzer(LanguageNames.CSharp)]`, `context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None)`, `context.EnableConcurrentExecution()`, and returns immediately when the compilation does not reference `UnambitiousFx.Synapse.Abstractions` (`SynapseSymbols.ReferencesSynapse`).
- Prefer false negatives: when a rule cannot decide, it does not report.
- Code style: file-scoped namespaces, always braces, XML `<summary>` on public types, comments explain why and sparingly.
- Tests: AAA with EXACT comments `// Arrange (Given)`, `// Act (When)`, `// Assert (Then)` each on its own line (never combined, never with appended text), names `Method_Scenario_ExpectedBehavior`. Run with MTP: `dotnet test --project test/Synapse.Generator.Tests -f net9.0 --filter-class "*ClassName"` (NOT `--filter`, which runs zero tests). Full run: `dotnet test --solution Synapse.slnx`.
- Do not touch the existing generator logic or tests; do not touch `docs/endpoints/` (untracked).
- Commit trailer: `Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>`.

---

### Task 1: Foundation and SYN103 (handler attribute on a record)

**Files:**
- Create: `src/Synapse.Generator/Analyzers/SynapseSymbols.cs`
- Create: `src/Synapse.Generator/Analyzers/HandlerAttributeOnRecordAnalyzer.cs`
- Create: `src/Synapse.Generator/AnalyzerReleases.Shipped.md`, `src/Synapse.Generator/AnalyzerReleases.Unshipped.md`
- Modify: `src/Synapse.Generator/Synapse.Generator.csproj` (AdditionalFiles)
- Create: `test/Synapse.Generator.Tests/Analyzers/AnalyzerTestHelper.cs`
- Test: `test/Synapse.Generator.Tests/Analyzers/HandlerAttributeOnRecordAnalyzerTests.cs`

**Interfaces:**
- Produces (`internal static class SynapseSymbols`, namespace `UnambitiousFx.Synapse.Generator.Analyzers`):
  - `const string AbstractionsNamespace = "UnambitiousFx.Synapse.Abstractions"`, `const string AbstractionsAssemblyName = "UnambitiousFx.Synapse.Abstractions"`
  - `static bool ReferencesSynapse(Compilation compilation)`
  - `static bool IsInAbstractions(ISymbol symbol)`
  - `static bool IsAttribute(INamedTypeSymbol? attributeClass, string attributeName)` (`attributeName` like `"PipelineBehaviorAttribute"`; must be in the Abstractions namespace)
  - `static bool IsHandlerAttribute(INamedTypeSymbol? attributeClass)` (RequestHandlerAttribute, EventHandlerAttribute, StreamRequestHandlerAttribute)
  - `static bool HasHandlerAttribute(ISymbol symbol)`
- Produces (test): `internal static class AnalyzerTestHelper` in namespace `UnambitiousFx.Synapse.Generator.Tests.Analyzers` with
  - `Task<ImmutableArray<Diagnostic>> RunAsync<TAnalyzer>(string source) where TAnalyzer : DiagnosticAnalyzer, new()`
  - `Task<ImmutableArray<Diagnostic>> RunAtPathAsync<TAnalyzer>(string source, string path)` (parses the main source with that file path, e.g. `"Handlers.g.cs"`)
  - `Task<ImmutableArray<Diagnostic>> RunWithReferenceAsync<TAnalyzer>(string referencedSource, string source)` (compiles `referencedSource` into a separate assembly `ReferencedAssembly`, referenced by the main one)
  - `const string Preamble` (usings: System.Threading, System.Threading.Tasks, UnambitiousFx.Functional, UnambitiousFx.Synapse.Abstractions)
  - All return ONLY analyzer diagnostics (not compiler diagnostics).

- [ ] **Step 1: Write the test helper and the failing tests**

`test/Synapse.Generator.Tests/Analyzers/AnalyzerTestHelper.cs`:
```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Generator.Tests.Analyzers;

/// <summary>
///     Runs one analyzer over minimal C# source. No analyzer-testing package is available, so this builds the
///     compilation by hand, like the generator tests do.
/// </summary>
internal static class AnalyzerTestHelper
{
    public const string Preamble = """
        using System.Threading;
        using System.Threading.Tasks;
        using UnambitiousFx.Functional;
        using UnambitiousFx.Synapse.Abstractions;

        """;

    private static readonly CSharpCompilationOptions Options =
        new(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable);

    public static Task<ImmutableArray<Diagnostic>> RunAsync<TAnalyzer>(string source)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        return RunCoreAsync<TAnalyzer>(source, "Test.cs", null);
    }

    public static Task<ImmutableArray<Diagnostic>> RunAtPathAsync<TAnalyzer>(string source, string path)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        return RunCoreAsync<TAnalyzer>(source, path, null);
    }

    public static Task<ImmutableArray<Diagnostic>> RunWithReferenceAsync<TAnalyzer>(string referencedSource,
        string source)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        return RunCoreAsync<TAnalyzer>(source, "Test.cs", referencedSource);
    }

    private static async Task<ImmutableArray<Diagnostic>> RunCoreAsync<TAnalyzer>(string source, string path,
        string? referencedSource)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var references = GetMetadataReferences().ToList();
        if (referencedSource is not null)
        {
            var referenced = CSharpCompilation.Create("ReferencedAssembly",
                [CSharpSyntaxTree.ParseText(referencedSource)], references, Options);
            references.Add(referenced.ToMetadataReference());
        }

        var compilation = CSharpCompilation.Create("TestAssembly",
            [CSharpSyntaxTree.ParseText(source, path: path)], references, Options);

        var withAnalyzers = compilation.WithAnalyzers([new TAnalyzer()]);
        return await withAnalyzers.GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
    }

    private static IEnumerable<MetadataReference> GetMetadataReferences()
    {
        var trustedPaths = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        var references = trustedPaths
            .Select(path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .ToList();
        references.Add(MetadataReference.CreateFromFile(typeof(PipelineBehaviorAttribute).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(UnambitiousFx.Functional.Result).Assembly.Location));
        return references;
    }
}
```
(If `Path.PathSeparator`/`TestContext.Current` need a using or differ, mirror `test/Synapse.Generator.Tests/GeneratorBehaviorTests.cs` `GetMetadataReferences()` at the bottom of that file.)

`test/Synapse.Generator.Tests/Analyzers/HandlerAttributeOnRecordAnalyzerTests.cs`:
```csharp
using JetBrains.Annotations;
using UnambitiousFx.Synapse.Generator.Analyzers;

namespace UnambitiousFx.Synapse.Generator.Tests.Analyzers;

[TestSubject(typeof(HandlerAttributeOnRecordAnalyzer))]
public sealed class HandlerAttributeOnRecordAnalyzerTests
{
    private const string Request = "public sealed record MyRequest : IRequest;\n";

    [Fact]
    public async Task Analyze_WithRequestHandlerAttributeOnARecord_ReportsSyn103OnTheAttribute()
    {
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + Request + """
            [RequestHandler<MyRequest>]
            public sealed record MyHandler : IRequestHandler<MyRequest>
            {
                public ValueTask<Result> HandleAsync(MyRequest request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success());
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<HandlerAttributeOnRecordAnalyzer>(source);

        // Assert (Then)
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("SYN103", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal("RequestHandler<MyRequest>", source.Substring(diagnostic.Location.SourceSpan.Start,
            diagnostic.Location.SourceSpan.Length));
    }

    [Fact]
    public async Task Analyze_WithEventHandlerAttributeOnARecord_ReportsSyn103()
    {
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + """
            public sealed record PingEvent : IEvent;

            [EventHandler<PingEvent>]
            public sealed record PingHandler : IEventHandler<PingEvent>
            {
                public ValueTask<Result> HandleAsync(PingEvent @event, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success());
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<HandlerAttributeOnRecordAnalyzer>(source);

        // Assert (Then)
        Assert.Equal("SYN103", Assert.Single(diagnostics).Id);
    }

    [Fact]
    public async Task Analyze_WithHandlerAttributeOnAClass_ReportsNothing()
    {
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + Request + """
            [RequestHandler<MyRequest>]
            public sealed class MyHandler : IRequestHandler<MyRequest>
            {
                public ValueTask<Result> HandleAsync(MyRequest request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success());
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<HandlerAttributeOnRecordAnalyzer>(source);

        // Assert (Then)
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Analyze_WithARecordThatHasNoHandlerAttribute_ReportsNothing()
    {
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + Request + """
            public sealed record MyHandler : IRequestHandler<MyRequest>
            {
                public ValueTask<Result> HandleAsync(MyRequest request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success());
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<HandlerAttributeOnRecordAnalyzer>(source);

        // Assert (Then)
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Analyze_WithAnUnrelatedAttributeOnARecord_ReportsNothing()
    {
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + Request + """
            [System.Obsolete]
            public sealed record MyHandler : IRequestHandler<MyRequest>
            {
                public ValueTask<Result> HandleAsync(MyRequest request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success());
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<HandlerAttributeOnRecordAnalyzer>(source);

        // Assert (Then)
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Analyze_WithoutAReferenceToSynapse_ReportsNothing()
    {
        // Arrange (Given)
        const string source = """
            [System.Obsolete]
            public sealed record Plain;
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<HandlerAttributeOnRecordAnalyzer>(source);

        // Assert (Then)
        Assert.Empty(diagnostics);
    }
}
```
The last test still compiles against the Synapse references (the helper always adds them), so it only proves the analyzer does not crash on unrelated code; that is acceptable and intended.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test --project test/Synapse.Generator.Tests -f net9.0 --filter-class "*HandlerAttributeOnRecordAnalyzerTests"`
Expected: build FAIL (`HandlerAttributeOnRecordAnalyzer` missing).

- [ ] **Step 3: Implement**

`src/Synapse.Generator/Synapse.Generator.csproj`: add an `ItemGroup`
```xml
    <ItemGroup>
        <AdditionalFiles Include="AnalyzerReleases.Shipped.md" />
        <AdditionalFiles Include="AnalyzerReleases.Unshipped.md" />
    </ItemGroup>
```
`src/Synapse.Generator/AnalyzerReleases.Shipped.md`:
```
; Shipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md
```
`src/Synapse.Generator/AnalyzerReleases.Unshipped.md`:
```
; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
SYN103 | Synapse.Analyzers | Warning | HandlerAttributeOnRecordAnalyzer
```
`src/Synapse.Generator/Analyzers/SynapseSymbols.cs`:
```csharp
using Microsoft.CodeAnalysis;

namespace UnambitiousFx.Synapse.Generator.Analyzers;

/// <summary>
///     Symbol lookups shared by the Synapse analyzers. Everything is matched by name in the Abstractions
///     namespace so the analyzers work against any version of the package.
/// </summary>
internal static class SynapseSymbols
{
    public const string AbstractionsNamespace = "UnambitiousFx.Synapse.Abstractions";
    public const string AbstractionsAssemblyName = "UnambitiousFx.Synapse.Abstractions";

    public static bool ReferencesSynapse(Compilation compilation)
    {
        return compilation.GetTypeByMetadataName($"{AbstractionsNamespace}.IRequest") is not null;
    }

    public static bool IsInAbstractions(ISymbol symbol)
    {
        return symbol.ContainingNamespace?.ToDisplayString() == AbstractionsNamespace;
    }

    public static bool IsAttribute(INamedTypeSymbol? attributeClass, string attributeName)
    {
        return attributeClass is not null && attributeClass.Name == attributeName && IsInAbstractions(attributeClass);
    }

    public static bool IsHandlerAttribute(INamedTypeSymbol? attributeClass)
    {
        return IsAttribute(attributeClass, "RequestHandlerAttribute")
               || IsAttribute(attributeClass, "EventHandlerAttribute")
               || IsAttribute(attributeClass, "StreamRequestHandlerAttribute");
    }

    public static bool HasHandlerAttribute(ISymbol symbol)
    {
        return symbol.GetAttributes().Any(attribute => IsHandlerAttribute(attribute.AttributeClass));
    }
}
```
`src/Synapse.Generator/Analyzers/HandlerAttributeOnRecordAnalyzer.cs`:
```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace UnambitiousFx.Synapse.Generator.Analyzers;

/// <summary>
///     SYN103: a handler attribute on a <c>record</c>. The source generator only accepts class declarations, so the
///     attribute is ignored without any error and the handler is never registered.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HandlerAttributeOnRecordAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "SYN103";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Handler attribute on a record is ignored",
        "[{0}] on record '{1}' is ignored by the Synapse source generator; declare the handler as a class",
        "Synapse.Analyzers",
        DiagnosticSeverity.Warning,
        true,
        "The Synapse source generator only registers handlers declared as classes. Declaring the handler as a record " +
        "leaves it unregistered without any other diagnostic.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            if (!SynapseSymbols.ReferencesSynapse(start.Compilation))
            {
                return;
            }

            start.RegisterSyntaxNodeAction(AnalyzeRecord, SyntaxKind.RecordDeclaration);
        });
    }

    private static void AnalyzeRecord(SyntaxNodeAnalysisContext context)
    {
        var record = (RecordDeclarationSyntax)context.Node;
        foreach (var attributeList in record.AttributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                var constructor = context.SemanticModel.GetSymbolInfo(attribute, context.CancellationToken).Symbol;
                if (constructor?.ContainingType is not { } attributeClass
                    || !SynapseSymbols.IsHandlerAttribute(attributeClass))
                {
                    continue;
                }

                var name = attributeClass.Name.EndsWith("Attribute", StringComparison.Ordinal)
                    ? attributeClass.Name.Substring(0, attributeClass.Name.Length - "Attribute".Length)
                    : attributeClass.Name;
                context.ReportDiagnostic(Diagnostic.Create(Rule, attribute.GetLocation(), name,
                    record.Identifier.ValueText));
            }
        }
    }
}
```
Message text uses `[RequestHandler]` (attribute name without suffix, no type arguments); the test checks the location span text (`RequestHandler<MyRequest>`), which is the attribute syntax.

- [ ] **Step 4: Run to verify pass**

Run the class filter again → 6 passed. Then `dotnet build Synapse.slnx` (0 warnings, 0 errors; fix any RS-series analyzer warnings properly) and `dotnet test --solution Synapse.slnx`.

- [ ] **Step 5: Commit**

```bash
git add src/Synapse.Generator test/Synapse.Generator.Tests/Analyzers
git commit -m "feat(analyzers): add SYN103 handler attribute on a record"
```

---

### Task 2: SYN101 (request without handler)

**Files:**
- Modify: `src/Synapse.Generator/Analyzers/SynapseSymbols.cs` (add `MessageInterface`, `HandlerKind`, `GetHandlerInterface`, `IsRequestInterface`)
- Create: `src/Synapse.Generator/Analyzers/RequestWithoutHandlerAnalyzer.cs`
- Modify: `src/Synapse.Generator/AnalyzerReleases.Unshipped.md` (add SYN101 row)
- Test: `test/Synapse.Generator.Tests/Analyzers/RequestWithoutHandlerAnalyzerTests.cs`

**Interfaces:**
- Consumes: `SynapseSymbols`, `AnalyzerTestHelper` (Task 1).
- Produces in `SynapseSymbols`:
  - `internal enum HandlerKind { Request, RequestWithResponse, Event, Stream }` (namespace `UnambitiousFx.Synapse.Generator.Analyzers`)
  - `internal readonly struct MessageInterface` with ctor `(HandlerKind kind, ITypeSymbol messageType)` and get-only properties `Kind`, `MessageType` (the first type argument: the request or event)
  - `static MessageInterface? GetHandlerInterface(INamedTypeSymbol iface)` — recognizes, in the Abstractions namespace, `IRequestHandler`1` (Request), `IRequestHandler`2` (RequestWithResponse), `IEventHandler`1` (Event), `IStreamRequestHandler`2` (Stream) by `MetadataName`; otherwise `null`.
  - `static bool IsRequestInterface(INamedTypeSymbol iface)` — Abstractions `IRequest` or `IRequest`1`.

- [ ] **Step 1: Write the failing tests**

```csharp
using JetBrains.Annotations;
using UnambitiousFx.Synapse.Generator.Analyzers;

namespace UnambitiousFx.Synapse.Generator.Tests.Analyzers;

[TestSubject(typeof(RequestWithoutHandlerAnalyzer))]
public sealed class RequestWithoutHandlerAnalyzerTests
{
    private const string VoidHandler = """
        public sealed class MyHandler : IRequestHandler<MyRequest>
        {
            public ValueTask<Result> HandleAsync(MyRequest request, CancellationToken ct = default)
                => ValueTask.FromResult(Result.Success());
        }
        """;

    [Fact]
    public async Task Analyze_WithARequestAndNoHandler_ReportsSyn101OnTheRequest()
    {
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + "public sealed record MyRequest : IRequest;";

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<RequestWithoutHandlerAnalyzer>(source);

        // Assert (Then)
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("SYN101", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("MyRequest", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Analyze_WithARequestAndItsHandler_ReportsNothing()
    {
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + "public sealed record MyRequest : IRequest;\n" + VoidHandler;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<RequestWithoutHandlerAnalyzer>(source);

        // Assert (Then)
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Analyze_WithAResponseRequestAndItsHandler_ReportsNothing()
    {
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + """
            public sealed record CountQuery : IRequest<int>;

            public sealed class CountHandler : IRequestHandler<CountQuery, int>
            {
                public ValueTask<Result<int>> HandleAsync(CountQuery request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success(1));
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<RequestWithoutHandlerAnalyzer>(source);

        // Assert (Then)
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Analyze_WithAHandlerForADifferentRequest_ReportsTheUnhandledOne()
    {
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + """
            public sealed record MyRequest : IRequest;

            public sealed record OtherRequest : IRequest;

            public sealed class OtherHandler : IRequestHandler<OtherRequest>
            {
                public ValueTask<Result> HandleAsync(OtherRequest request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success());
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<RequestWithoutHandlerAnalyzer>(source);

        // Assert (Then)
        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("MyRequest", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Analyze_WithAnAbstractRequest_ReportsNothing()
    {
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + "public abstract record BaseRequest : IRequest;";

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<RequestWithoutHandlerAnalyzer>(source);

        // Assert (Then)
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Analyze_WithAnOpenGenericRequest_ReportsNothing()
    {
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + "public sealed record Wrapped<T> : IRequest;";

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<RequestWithoutHandlerAnalyzer>(source);

        // Assert (Then)
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Analyze_WithAnOpenGenericHandler_ReportsNothing()
    {
        // Arrange (Given) — a generic handler may cover any request, so the rule cannot decide
        var source = AnalyzerTestHelper.Preamble + """
            public sealed record MyRequest : IRequest;

            public sealed class AnyHandler<TRequest> : IRequestHandler<TRequest>
                where TRequest : IRequest
            {
                public ValueTask<Result> HandleAsync(TRequest request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success());
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<RequestWithoutHandlerAnalyzer>(source);

        // Assert (Then)
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Analyze_WithAnEventAndNoHandler_ReportsNothing()
    {
        // Arrange (Given) — zero subscribers is legal for an event
        var source = AnalyzerTestHelper.Preamble + "public sealed record PingEvent : IEvent;";

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<RequestWithoutHandlerAnalyzer>(source);

        // Assert (Then)
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Analyze_WithARequestInGeneratedCode_ReportsNothing()
    {
        // Arrange (Given)
        var source = "// <auto-generated/>\n" + AnalyzerTestHelper.Preamble +
                     "public sealed record MyRequest : IRequest;";

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAtPathAsync<RequestWithoutHandlerAnalyzer>(source, "Requests.g.cs");

        // Assert (Then)
        Assert.Empty(diagnostics);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test --project test/Synapse.Generator.Tests -f net9.0 --filter-class "*RequestWithoutHandlerAnalyzerTests"` → build FAIL.

- [ ] **Step 3: Implement**

Append to `SynapseSymbols.cs` (types before the class, method bodies inside):
```csharp
internal enum HandlerKind
{
    Request,
    RequestWithResponse,
    Event,
    Stream
}

internal readonly struct MessageInterface
{
    public MessageInterface(HandlerKind kind, ITypeSymbol messageType)
    {
        Kind = kind;
        MessageType = messageType;
    }

    public HandlerKind Kind { get; }

    public ITypeSymbol MessageType { get; }
}
```
and in the class:
```csharp
    public static MessageInterface? GetHandlerInterface(INamedTypeSymbol iface)
    {
        if (!IsInAbstractions(iface))
        {
            return null;
        }

        return iface.MetadataName switch
        {
            "IRequestHandler`1" => new MessageInterface(HandlerKind.Request, iface.TypeArguments[0]),
            "IRequestHandler`2" => new MessageInterface(HandlerKind.RequestWithResponse, iface.TypeArguments[0]),
            "IEventHandler`1" => new MessageInterface(HandlerKind.Event, iface.TypeArguments[0]),
            "IStreamRequestHandler`2" => new MessageInterface(HandlerKind.Stream, iface.TypeArguments[0]),
            _ => null
        };
    }

    public static bool IsRequestInterface(INamedTypeSymbol iface)
    {
        return IsInAbstractions(iface) && iface.MetadataName is "IRequest" or "IRequest`1";
    }
```
`src/Synapse.Generator/Analyzers/RequestWithoutHandlerAnalyzer.cs`:
```csharp
using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace UnambitiousFx.Synapse.Generator.Analyzers;

/// <summary>
///     SYN101: a request type with no handler in this compilation. Only the current assembly is scanned, so a handler
///     that lives in another project is a false positive; disable the rule there with
///     <c>dotnet_diagnostic.SYN101.severity = none</c>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RequestWithoutHandlerAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "SYN101";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Request has no handler in this assembly",
        "Request '{0}' has no handler in this assembly",
        "Synapse.Analyzers",
        DiagnosticSeverity.Warning,
        true,
        "No type in this assembly implements IRequestHandler for this request, so invoking it fails at runtime. If " +
        "the handler lives in another assembly, disable this rule with dotnet_diagnostic.SYN101.severity = none.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            if (!SynapseSymbols.ReferencesSynapse(start.Compilation))
            {
                return;
            }

            var requests = new ConcurrentBag<INamedTypeSymbol>();
            var handled = new ConcurrentDictionary<ISymbol, byte>(SymbolEqualityComparer.Default);
            var hasGenericHandler = 0;

            start.RegisterSymbolAction(symbolContext =>
            {
                var type = (INamedTypeSymbol)symbolContext.Symbol;
                if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct))
                {
                    return;
                }

                if (!type.IsAbstract && !type.IsGenericType && type.AllInterfaces.Any(SynapseSymbols.IsRequestInterface))
                {
                    requests.Add(type);
                }

                foreach (var iface in type.AllInterfaces)
                {
                    var handler = SynapseSymbols.GetHandlerInterface(iface);
                    if (handler is not { Kind: HandlerKind.Request or HandlerKind.RequestWithResponse } request)
                    {
                        continue;
                    }

                    if (request.MessageType is ITypeParameterSymbol || request.MessageType is INamedTypeSymbol { IsGenericType: true })
                    {
                        Interlocked.Exchange(ref hasGenericHandler, 1);
                        continue;
                    }

                    handled.TryAdd(request.MessageType, 0);
                }
            }, SymbolKind.NamedType);

            start.RegisterCompilationEndAction(endContext =>
            {
                if (hasGenericHandler == 1)
                {
                    return;
                }

                foreach (var request in requests)
                {
                    if (handled.ContainsKey(request))
                    {
                        continue;
                    }

                    var location = request.Locations.FirstOrDefault(candidate => candidate.IsInSource);
                    if (location is null)
                    {
                        continue;
                    }

                    endContext.ReportDiagnostic(Diagnostic.Create(Rule, location, request.Name));
                }
            });
        });
    }
}
```
`handler is not { Kind: ... } request` pattern: `MessageInterface?` is `Nullable<MessageInterface>`; property patterns work on nullable structs (`handler is { Kind: ... } request` binds the unwrapped value). If the pattern form does not compile on your compiler, rewrite with `if (handler is null) continue; var request = handler.Value;`.
Add to `AnalyzerReleases.Unshipped.md` table: `SYN101 | Synapse.Analyzers | Warning | RequestWithoutHandlerAnalyzer`.

- [ ] **Step 4: Run to verify pass**

Class filter → 9 passed. Mutation check: temporarily make the abstract exclusion `!type.IsAbstract` → `true` and confirm the abstract-request test fails, then revert. `dotnet build Synapse.slnx` 0 warnings; full test run green.

- [ ] **Step 5: Commit**

```bash
git add src/Synapse.Generator test/Synapse.Generator.Tests/Analyzers
git commit -m "feat(analyzers): add SYN101 request without handler"
```

---

### Task 3: SYN102 (behavior applies to no handler)

**Files:**
- Modify: `src/Synapse.Generator/Analyzers/SynapseSymbols.cs` (add `GetBehaviorInterface`, `GetTypes`)
- Create: `src/Synapse.Generator/Analyzers/BehaviorWithoutHandlersAnalyzer.cs`
- Modify: `src/Synapse.Generator/AnalyzerReleases.Unshipped.md` (SYN102 row)
- Test: `test/Synapse.Generator.Tests/Analyzers/BehaviorWithoutHandlersAnalyzerTests.cs`

**Interfaces:**
- Consumes: `SynapseSymbols.IsAttribute`, `GetHandlerInterface`, `MessageInterface`, `HandlerKind`, `AbstractionsAssemblyName` (Tasks 1–2); `AnalyzerTestHelper.RunAsync` / `RunWithReferenceAsync`.
- Produces in `SynapseSymbols`:
  - `static MessageInterface? GetBehaviorInterface(INamedTypeSymbol iface)` — Abstractions `IRequestPipelineBehavior`1` (Request), `IRequestPipelineBehavior`2` (RequestWithResponse), `IEventPipelineBehavior`1` (Event), `IStreamRequestPipelineBehavior`2` (Stream); `MessageType` = first type argument.
  - `static IEnumerable<INamedTypeSymbol> GetTypes(INamespaceSymbol root, CancellationToken cancellationToken)` — every named type in the namespace tree including nested types.

- [ ] **Step 1: Write the failing tests**

```csharp
using JetBrains.Annotations;
using UnambitiousFx.Synapse.Generator.Analyzers;

namespace UnambitiousFx.Synapse.Generator.Tests.Analyzers;

[TestSubject(typeof(BehaviorWithoutHandlersAnalyzer))]
public sealed class BehaviorWithoutHandlersAnalyzerTests
{
    private const string MyRequestAndHandler = """
        public sealed record MyRequest : IRequest;

        [RequestHandler<MyRequest>]
        public sealed class MyHandler : IRequestHandler<MyRequest>
        {
            public ValueTask<Result> HandleAsync(MyRequest request, CancellationToken ct = default)
                => ValueTask.FromResult(Result.Success());
        }

        """;

    private const string GenericBehavior = """
        [PipelineBehavior]
        public sealed class LoggingBehavior<TRequest> : IRequestPipelineBehavior<TRequest>
            where TRequest : IRequest
        {
            public ValueTask<Result> HandleAsync(TRequest request, RequestHandlerDelegate<TRequest> next,
                CancellationToken ct = default) => next(request, ct);
        }
        """;

    [Fact]
    public async Task Analyze_WithAnOpenGenericBehaviorAndAMatchingHandler_ReportsNothing()
    {
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + MyRequestAndHandler + GenericBehavior;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<BehaviorWithoutHandlersAnalyzer>(source);

        // Assert (Then)
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Analyze_WithAnOpenGenericBehaviorAndNoHandlerAtAll_ReportsSyn102OnTheBehavior()
    {
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + GenericBehavior;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<BehaviorWithoutHandlersAnalyzer>(source);

        // Assert (Then)
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("SYN102", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("LoggingBehavior", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Analyze_WithAClosedBehaviorForARequestThatHasNoHandler_ReportsSyn102()
    {
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + MyRequestAndHandler + """
            public sealed record OtherRequest : IRequest;

            [PipelineBehavior]
            public sealed class OtherBehavior : IRequestPipelineBehavior<OtherRequest>
            {
                public ValueTask<Result> HandleAsync(OtherRequest request, RequestHandlerDelegate<OtherRequest> next,
                    CancellationToken ct = default) => next(request, ct);
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<BehaviorWithoutHandlersAnalyzer>(source);

        // Assert (Then)
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("SYN102", diagnostic.Id);
        Assert.Contains("OtherBehavior", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Analyze_WithAClosedBehaviorForARequestThatHasAHandler_ReportsNothing()
    {
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + MyRequestAndHandler + """
            [PipelineBehavior]
            public sealed class MyBehavior : IRequestPipelineBehavior<MyRequest>
            {
                public ValueTask<Result> HandleAsync(MyRequest request, RequestHandlerDelegate<MyRequest> next,
                    CancellationToken ct = default) => next(request, ct);
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<BehaviorWithoutHandlersAnalyzer>(source);

        // Assert (Then)
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Analyze_WithABehaviorWhoseConstraintNoHandlerSatisfies_ReportsSyn102()
    {
        // Arrange (Given) — the only handler handles a request that is not an IAuditedRequest
        var source = AnalyzerTestHelper.Preamble + MyRequestAndHandler + """
            public interface IAuditedRequest : IRequest;

            [PipelineBehavior]
            public sealed class AuditBehavior<TRequest> : IRequestPipelineBehavior<TRequest>
                where TRequest : IAuditedRequest
            {
                public ValueTask<Result> HandleAsync(TRequest request, RequestHandlerDelegate<TRequest> next,
                    CancellationToken ct = default) => next(request, ct);
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<BehaviorWithoutHandlersAnalyzer>(source);

        // Assert (Then)
        Assert.Equal("SYN102", Assert.Single(diagnostics).Id);
    }

    [Fact]
    public async Task Analyze_WithABehaviorThatHasSpecialConstraintsAndNoHandler_ReportsNothing()
    {
        // Arrange (Given) — the rule cannot evaluate 'class', so it stays silent rather than guess
        var source = AnalyzerTestHelper.Preamble + """
            [PipelineBehavior]
            public sealed class ClassOnlyBehavior<TRequest> : IRequestPipelineBehavior<TRequest>
                where TRequest : class, IRequest
            {
                public ValueTask<Result> HandleAsync(TRequest request, RequestHandlerDelegate<TRequest> next,
                    CancellationToken ct = default) => next(request, ct);
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<BehaviorWithoutHandlersAnalyzer>(source);

        // Assert (Then)
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Analyze_WithAGlobalBehaviorEntryAndNoHandler_ReportsSyn102OnTheAttribute()
    {
        // Arrange (Given)
        var source = "[assembly: UnambitiousFx.Synapse.Abstractions.SynapseGlobalBehavior(typeof(LoggingBehavior<>))]\n" +
                     AnalyzerTestHelper.Preamble + GenericBehavior.Replace("[PipelineBehavior]\n", string.Empty);

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<BehaviorWithoutHandlersAnalyzer>(source);

        // Assert (Then)
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("SYN102", diagnostic.Id);
        Assert.Contains("LoggingBehavior", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Analyze_WithAGlobalBehaviorEntryAndAMatchingHandler_ReportsNothing()
    {
        // Arrange (Given)
        var source = "[assembly: UnambitiousFx.Synapse.Abstractions.SynapseGlobalBehavior(typeof(LoggingBehavior<>))]\n" +
                     AnalyzerTestHelper.Preamble + MyRequestAndHandler +
                     GenericBehavior.Replace("[PipelineBehavior]\n", string.Empty);

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<BehaviorWithoutHandlersAnalyzer>(source);

        // Assert (Then)
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Analyze_WithAHandlerInAReferencedAssembly_ReportsNothing()
    {
        // Arrange (Given) — the behavior propagates to handlers of the assemblies this one references
        var referenced = AnalyzerTestHelper.Preamble + MyRequestAndHandler;
        var source = AnalyzerTestHelper.Preamble + GenericBehavior;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunWithReferenceAsync<BehaviorWithoutHandlersAnalyzer>(referenced, source);

        // Assert (Then)
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Analyze_WithAnEventBehaviorAndNoEventHandler_ReportsSyn102()
    {
        // Arrange (Given) — a request handler must not satisfy an event behavior
        var source = AnalyzerTestHelper.Preamble + MyRequestAndHandler + """
            public sealed record PingEvent : IEvent;

            [PipelineBehavior]
            public sealed class PingBehavior : IEventPipelineBehavior<PingEvent>
            {
                public ValueTask<Result> HandleAsync(PingEvent @event, EventHandlerDelegate<PingEvent> next,
                    CancellationToken ct = default) => next(@event, ct);
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<BehaviorWithoutHandlersAnalyzer>(source);

        // Assert (Then)
        Assert.Equal("SYN102", Assert.Single(diagnostics).Id);
    }

    [Fact]
    public async Task Analyze_WithAnEventBehaviorAndAnEventHandler_ReportsNothing()
    {
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + """
            public sealed record PingEvent : IEvent;

            [EventHandler<PingEvent>]
            public sealed class PingHandler : IEventHandler<PingEvent>
            {
                public ValueTask<Result> HandleAsync(PingEvent @event, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success());
            }

            [PipelineBehavior]
            public sealed class PingBehavior : IEventPipelineBehavior<PingEvent>
            {
                public ValueTask<Result> HandleAsync(PingEvent @event, EventHandlerDelegate<PingEvent> next,
                    CancellationToken ct = default) => next(@event, ct);
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<BehaviorWithoutHandlersAnalyzer>(source);

        // Assert (Then)
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Analyze_WithAPipelineBehaviorThatImplementsNoPipelineInterface_ReportsNothing()
    {
        // Arrange (Given) — the generator already reports this one (MDG008)
        var source = AnalyzerTestHelper.Preamble + """
            [PipelineBehavior]
            public sealed class NotABehavior;
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<BehaviorWithoutHandlersAnalyzer>(source);

        // Assert (Then)
        Assert.Empty(diagnostics);
    }
}
```
If the assembly-level attribute placement fails to compile (usings must come before the attribute in C#: `using` directives precede attribute lists), reorder so the `Preamble` usings come first and the `[assembly: ...]` line follows them; keep the assertions.

- [ ] **Step 2: Run to verify failure**

`dotnet test --project test/Synapse.Generator.Tests -f net9.0 --filter-class "*BehaviorWithoutHandlersAnalyzerTests"` → build FAIL.

- [ ] **Step 3: Implement**

Append to `SynapseSymbols`:
```csharp
    public static MessageInterface? GetBehaviorInterface(INamedTypeSymbol iface)
    {
        if (!IsInAbstractions(iface))
        {
            return null;
        }

        return iface.MetadataName switch
        {
            "IRequestPipelineBehavior`1" => new MessageInterface(HandlerKind.Request, iface.TypeArguments[0]),
            "IRequestPipelineBehavior`2" =>
                new MessageInterface(HandlerKind.RequestWithResponse, iface.TypeArguments[0]),
            "IEventPipelineBehavior`1" => new MessageInterface(HandlerKind.Event, iface.TypeArguments[0]),
            "IStreamRequestPipelineBehavior`2" => new MessageInterface(HandlerKind.Stream, iface.TypeArguments[0]),
            _ => null
        };
    }

    public static IEnumerable<INamedTypeSymbol> GetTypes(INamespaceSymbol root, CancellationToken cancellationToken)
    {
        foreach (var member in root.GetMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (member is INamespaceSymbol child)
            {
                foreach (var type in GetTypes(child, cancellationToken))
                {
                    yield return type;
                }
            }
            else if (member is INamedTypeSymbol named)
            {
                yield return named;
                foreach (var nested in GetNested(named))
                {
                    yield return nested;
                }
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> GetNested(INamedTypeSymbol type)
    {
        foreach (var nested in type.GetTypeMembers())
        {
            yield return nested;
            foreach (var deeper in GetNested(nested))
            {
                yield return deeper;
            }
        }
    }
```
`src/Synapse.Generator/Analyzers/BehaviorWithoutHandlersAnalyzer.cs`:
```csharp
using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace UnambitiousFx.Synapse.Generator.Analyzers;

/// <summary>
///     SYN102: a <c>[PipelineBehavior]</c> class or an <c>[assembly: SynapseGlobalBehavior]</c> entry that the generator
///     would emit for no handler, so it silently never runs. Handlers visible to a behavior are those of this
///     compilation plus those of referenced assemblies that themselves reference Synapse.Abstractions. When a
///     behavior cannot be evaluated (special constraints, constraints over other type parameters) it is not reported.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BehaviorWithoutHandlersAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "SYN102";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Pipeline behavior applies to no handler",
        "Pipeline behavior '{0}' matches no handler visible from this assembly, so it never runs",
        "Synapse.Analyzers",
        DiagnosticSeverity.Warning,
        true,
        "Behaviors are only registered for handlers found in this assembly and the assemblies it references. Check " +
        "the request or event type the behavior targets and the handlers this assembly can see.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            if (!SynapseSymbols.ReferencesSynapse(start.Compilation))
            {
                return;
            }

            var behaviors = new ConcurrentBag<INamedTypeSymbol>();
            var sourceHandlers = new ConcurrentBag<MessageInterface>();

            start.RegisterSymbolAction(symbolContext =>
            {
                var type = (INamedTypeSymbol)symbolContext.Symbol;
                if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct))
                {
                    return;
                }

                if (type.GetAttributes().Any(a => SynapseSymbols.IsAttribute(a.AttributeClass, "PipelineBehaviorAttribute")))
                {
                    behaviors.Add(type);
                }

                if (type.IsAbstract)
                {
                    return;
                }

                foreach (var iface in type.AllInterfaces)
                {
                    if (SynapseSymbols.GetHandlerInterface(iface) is { } handler)
                    {
                        sourceHandlers.Add(handler);
                    }
                }
            }, SymbolKind.NamedType);

            start.RegisterCompilationEndAction(endContext =>
            {
                var globalEntries = new List<(INamedTypeSymbol Type, Location Location)>();
                foreach (var attribute in endContext.Compilation.Assembly.GetAttributes())
                {
                    if (!SynapseSymbols.IsAttribute(attribute.AttributeClass, "SynapseGlobalBehaviorAttribute")
                        || attribute.ConstructorArguments.Length != 1
                        || attribute.ConstructorArguments[0].Value is not INamedTypeSymbol behaviorType)
                    {
                        continue;
                    }

                    var location = attribute.ApplicationSyntaxReference?
                        .GetSyntax(endContext.CancellationToken).GetLocation() ?? Location.None;
                    globalEntries.Add((behaviorType.OriginalDefinition, location));
                }

                if (behaviors.IsEmpty && globalEntries.Count == 0)
                {
                    return;
                }

                var handlers = sourceHandlers.ToList();
                handlers.AddRange(ReferencedHandlers(endContext.Compilation, endContext.CancellationToken));

                foreach (var behavior in behaviors)
                {
                    var location = behavior.Locations.FirstOrDefault(candidate => candidate.IsInSource);
                    if (location is not null)
                    {
                        Check(endContext, behavior, location, handlers);
                    }
                }

                foreach (var (type, location) in globalEntries)
                {
                    Check(endContext, type, location, handlers);
                }
            });
        });
    }

    private static void Check(CompilationAnalysisContext context, INamedTypeSymbol behavior, Location location,
        List<MessageInterface> handlers)
    {
        var recognized = false;
        foreach (var iface in behavior.AllInterfaces)
        {
            if (SynapseSymbols.GetBehaviorInterface(iface) is not { } pipeline)
            {
                continue;
            }

            recognized = true;
            if (MayApply(context.Compilation, pipeline, handlers))
            {
                return;
            }
        }

        if (recognized)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, location, behavior.Name));
        }
    }

    private static bool MayApply(Compilation compilation, MessageInterface pipeline, List<MessageInterface> handlers)
    {
        var candidates = handlers.Where(handler => handler.Kind == pipeline.Kind).ToList();
        var target = pipeline.MessageType;

        if (target is ITypeParameterSymbol parameter)
        {
            if (parameter.HasReferenceTypeConstraint || parameter.HasValueTypeConstraint
                                                     || parameter.HasConstructorConstraint
                                                     || parameter.HasNotNullConstraint
                                                     || parameter.HasUnmanagedTypeConstraint)
            {
                return true;
            }

            var constraints = parameter.ConstraintTypes.Where(constraint => !ContainsTypeParameter(constraint)).ToList();
            return candidates.Any(handler => constraints.All(constraint =>
                compilation.ClassifyCommonConversion(handler.MessageType, constraint).IsImplicit));
        }

        if (ContainsTypeParameter(target))
        {
            return true;
        }

        return candidates.Any(handler => SymbolEqualityComparer.Default.Equals(handler.MessageType, target));
    }

    private static bool ContainsTypeParameter(ITypeSymbol type)
    {
        return type switch
        {
            ITypeParameterSymbol => true,
            INamedTypeSymbol named => named.TypeArguments.Any(ContainsTypeParameter),
            IArrayTypeSymbol array => ContainsTypeParameter(array.ElementType),
            _ => false
        };
    }

    private static IEnumerable<MessageInterface> ReferencedHandlers(Compilation compilation,
        CancellationToken cancellationToken)
    {
        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            if (assembly.Name == SynapseSymbols.AbstractionsAssemblyName
                || !assembly.Modules.Any(module => module.ReferencedAssemblies.Any(reference =>
                    reference.Name == SynapseSymbols.AbstractionsAssemblyName)))
            {
                continue;
            }

            foreach (var type in SynapseSymbols.GetTypes(assembly.GlobalNamespace, cancellationToken))
            {
                if (type.TypeKind != TypeKind.Class || type.IsAbstract)
                {
                    continue;
                }

                foreach (var iface in type.AllInterfaces)
                {
                    if (SynapseSymbols.GetHandlerInterface(iface) is { } handler)
                    {
                        yield return handler;
                    }
                }
            }
        }
    }
}
```
Notes for the implementer: `Compilation.ClassifyCommonConversion` and the `ITypeParameterSymbol.Has*Constraint` members exist in the referenced Roslyn; if `ClassifyCommonConversion` is unavailable use `((CSharpCompilation)compilation).ClassifyConversion(...)` guarded by a type test (return `true` = "may apply" when the compilation is not a `CSharpCompilation`). If `attribute.ConstructorArguments[0].Value` is not an `INamedTypeSymbol` for `typeof(B<>)` on your Roslyn, print what it is and adapt; the goal is the unbound generic's `OriginalDefinition`.
Add to `AnalyzerReleases.Unshipped.md`: `SYN102 | Synapse.Analyzers | Warning | BehaviorWithoutHandlersAnalyzer`.

- [ ] **Step 4: Run to verify pass**

Class filter → 11 passed. Mutation check: make `MayApply` always `return true` → the two "ReportsSyn102" behavior tests fail; revert. Then `dotnet build Synapse.slnx` (0 warnings) and the full test run.

- [ ] **Step 5: Commit**

```bash
git add src/Synapse.Generator test/Synapse.Generator.Tests/Analyzers
git commit -m "feat(analyzers): add SYN102 behavior applies to no handler"
```

---

### Task 4: SYN104 (unattributed handler in an attribute-using assembly)

**Files:**
- Create: `src/Synapse.Generator/Analyzers/UnattributedHandlerAnalyzer.cs`
- Modify: `src/Synapse.Generator/AnalyzerReleases.Unshipped.md` (SYN104 row)
- Test: `test/Synapse.Generator.Tests/Analyzers/UnattributedHandlerAnalyzerTests.cs`

**Interfaces:**
- Consumes: `SynapseSymbols.ReferencesSynapse`, `GetHandlerInterface`, `HasHandlerAttribute`; `AnalyzerTestHelper.RunAsync`, `RunAtPathAsync`.

- [ ] **Step 1: Write the failing tests**

```csharp
using JetBrains.Annotations;
using UnambitiousFx.Synapse.Generator.Analyzers;

namespace UnambitiousFx.Synapse.Generator.Tests.Analyzers;

[TestSubject(typeof(UnattributedHandlerAnalyzer))]
public sealed class UnattributedHandlerAnalyzerTests
{
    private const string Attributed = """
        public sealed record FirstRequest : IRequest;

        [RequestHandler<FirstRequest>]
        public sealed class FirstHandler : IRequestHandler<FirstRequest>
        {
            public ValueTask<Result> HandleAsync(FirstRequest request, CancellationToken ct = default)
                => ValueTask.FromResult(Result.Success());
        }

        """;

    private const string Unattributed = """
        public sealed record SecondRequest : IRequest;

        public sealed class SecondHandler : IRequestHandler<SecondRequest>
        {
            public ValueTask<Result> HandleAsync(SecondRequest request, CancellationToken ct = default)
                => ValueTask.FromResult(Result.Success());
        }
        """;

    [Fact]
    public async Task Analyze_WithAnUnattributedHandlerNextToAnAttributedOne_ReportsSyn104OnTheUnattributedClass()
    {
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + Attributed + Unattributed;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<UnattributedHandlerAnalyzer>(source);

        // Assert (Then)
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("SYN104", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("SecondHandler", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Analyze_WithNoAttributedHandlerAtAll_ReportsNothing()
    {
        // Arrange (Given) — an assembly that registers by hand is not using the generator
        var source = AnalyzerTestHelper.Preamble + Unattributed;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<UnattributedHandlerAnalyzer>(source);

        // Assert (Then)
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Analyze_WithEveryHandlerAttributed_ReportsNothing()
    {
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + Attributed;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<UnattributedHandlerAnalyzer>(source);

        // Assert (Then)
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Analyze_WithAnUnattributedEventHandler_ReportsSyn104()
    {
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + Attributed + """
            public sealed record PingEvent : IEvent;

            public sealed class PingHandler : IEventHandler<PingEvent>
            {
                public ValueTask<Result> HandleAsync(PingEvent @event, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success());
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<UnattributedHandlerAnalyzer>(source);

        // Assert (Then)
        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("PingHandler", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Analyze_WithAnAbstractUnattributedHandlerBase_ReportsNothing()
    {
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + Attributed + """
            public sealed record ThirdRequest : IRequest;

            public abstract class HandlerBase : IRequestHandler<ThirdRequest>
            {
                public abstract ValueTask<Result> HandleAsync(ThirdRequest request, CancellationToken ct = default);
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<UnattributedHandlerAnalyzer>(source);

        // Assert (Then)
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Analyze_WithAnUnattributedHandlerInGeneratedCode_ReportsNothing()
    {
        // Arrange (Given)
        var source = "// <auto-generated/>\n" + AnalyzerTestHelper.Preamble + Attributed + Unattributed;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAtPathAsync<UnattributedHandlerAnalyzer>(source, "Handlers.g.cs");

        // Assert (Then)
        Assert.Empty(diagnostics);
    }
}
```

- [ ] **Step 2: Run to verify failure**

`dotnet test --project test/Synapse.Generator.Tests -f net9.0 --filter-class "*UnattributedHandlerAnalyzerTests"` → build FAIL.

- [ ] **Step 3: Implement**

`src/Synapse.Generator/Analyzers/UnattributedHandlerAnalyzer.cs`:
```csharp
using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace UnambitiousFx.Synapse.Generator.Analyzers;

/// <summary>
///     SYN104: in an assembly where handlers are registered through the source generator's attributes, a handler class
///     without an attribute is left out of the generated registration. Manual registration cannot be seen, so this is a
///     heuristic; silence it for a hand-registered handler with <c>#pragma warning disable SYN104</c> or
///     <c>dotnet_diagnostic.SYN104.severity = none</c>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnattributedHandlerAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "SYN104";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Handler is not registered by the source generator",
        "Handler '{0}' has no handler attribute, so the Synapse source generator does not register it. Add " +
        "[RequestHandler], [EventHandler] or [StreamRequestHandler], or register it by hand and silence this warning",
        "Synapse.Analyzers",
        DiagnosticSeverity.Warning,
        true,
        "Other handlers in this assembly use the generator's attributes, so a handler without one is easy to forget.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            if (!SynapseSymbols.ReferencesSynapse(start.Compilation))
            {
                return;
            }

            var unattributed = new ConcurrentBag<INamedTypeSymbol>();
            var anyAttributed = 0;

            start.RegisterSymbolAction(symbolContext =>
            {
                var type = (INamedTypeSymbol)symbolContext.Symbol;
                if (type.TypeKind != TypeKind.Class)
                {
                    return;
                }

                if (SynapseSymbols.HasHandlerAttribute(type))
                {
                    Interlocked.Exchange(ref anyAttributed, 1);
                    return;
                }

                if (type.IsAbstract || type.IsGenericType)
                {
                    return;
                }

                if (type.AllInterfaces.Any(iface => SynapseSymbols.GetHandlerInterface(iface) is not null))
                {
                    unattributed.Add(type);
                }
            }, SymbolKind.NamedType);

            start.RegisterCompilationEndAction(endContext =>
            {
                if (anyAttributed == 0)
                {
                    return;
                }

                foreach (var type in unattributed)
                {
                    var location = type.Locations.FirstOrDefault(candidate => candidate.IsInSource);
                    if (location is not null)
                    {
                        endContext.ReportDiagnostic(Diagnostic.Create(Rule, location, type.Name));
                    }
                }
            });
        });
    }
}
```
Add to `AnalyzerReleases.Unshipped.md`: `SYN104 | Synapse.Analyzers | Warning | UnattributedHandlerAnalyzer`.

- [ ] **Step 4: Run to verify pass**

Class filter → 6 passed. Mutation check: drop the `anyAttributed == 0` early return → `…WithNoAttributedHandlerAtAll…` fails; revert. `dotnet build Synapse.slnx` (0 warnings), full tests.

- [ ] **Step 5: Commit**

```bash
git add src/Synapse.Generator test/Synapse.Generator.Tests/Analyzers
git commit -m "feat(analyzers): add SYN104 unattributed handler"
```

---

### Task 5: Consumers, docs and full verification

**Files:**
- Modify: `docs/docs/source-generator.mdx` (new "Diagnostics" section before `## See also`)
- Possibly modify: files under `examples/` only if a rule reports a REAL problem there (see Step 1)

**Interfaces:**
- Consumes: the four analyzers. Projects that load the generator as an analyzer and therefore now run them: `examples/MinimalApi`, `examples/MinimalApi.Modules.Orders`, `examples/MinimalApi.Modules.Notifications`, `examples/GettingStarted` (all have warnings-as-errors through `build.props`).

- [ ] **Step 1: Build every consumer**

Run: `dotnet build Synapse.slnx` and read every `SYN1xx` diagnostic reported. For each one decide:
- the analyzer is wrong (a false positive that a user could hit too, for example a handler registered from another assembly of the same example) → fix the analyzer with a regression test in that analyzer's test class, do NOT suppress it in the example;
- the example really has the problem → fix the example;
- unclear (for example a deliberate request-without-handler in a modular example whose handler lives in a sibling project) → STOP and report DONE_WITH_CONCERNS with the exact diagnostics (id, file, message) so the controller can decide; do not silence anything on your own.
Expected: zero `SYN1xx` diagnostics in the four examples.

- [ ] **Step 2: Docs**

In `docs/docs/source-generator.mdx` add `## Diagnostics` before `## See also`: a table (ID, when it fires, fix) for SYN101–SYN104 matching the spec; one paragraph per rule only if the fix is not obvious; an `.editorconfig` block
```
[*.cs]
dotnet_diagnostic.SYN101.severity = none
```
with the note that SYN101 only sees the current assembly (a handler in another project needs the rule disabled there) and that SYN104 cannot see manual registration; a sentence that the analyzers ship in the same `UnambitiousFx.Synapse.Generator` package and are disabled per rule with `severity = none`; a sentence that they do not detect a host that never registers its generated group (`AddRegisterGroup`), which the runtime `ValidateSynapse()` cannot detect either (#91). Then `cd docs && pnpm build` → success, zero broken-link warnings.

- [ ] **Step 3: Full verification**

Run `dotnet build Synapse.slnx` (0 warnings, 0 errors) and `dotnet test --solution Synapse.slnx` (all green). Confirm `git status` shows only intended files (never `docs/endpoints/`).

- [ ] **Step 4: Commit**

```bash
git add docs/docs/source-generator.mdx
git commit -m "docs(analyzers): document the SYN101-SYN104 diagnostics"
```
(include any example or analyzer fixes from Step 1 in the same or a preceding commit with a clear message)
