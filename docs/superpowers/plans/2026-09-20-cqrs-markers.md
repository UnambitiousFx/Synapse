# ICommand / IQuery Markers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `ICommand`, `ICommand<TResponse>`, `IQuery<TResponse>` and the thin handler aliases `ICommandHandler<>`, `ICommandHandler<,>`, `IQueryHandler<,>` to `Synapse.Abstractions`, and prove they work with the runtime, the source generator, `ValidateSynapse()` and the SYN analyzers.

**Architecture:** Pure additive interfaces inheriting `IRequest` / `IRequest<T>` / `IRequestHandler<>`. No new attribute, package or registration API: behaviors are scoped by generic constraints (`where TRequest : ICommand<TResponse>`), a mechanism the generator and DI already support. The plan is mostly tests that PROVE that claim, plus docs.

**Tech Stack:** .NET 8/9/10 multi-target, xUnit v3 on Microsoft Testing Platform, Roslyn (generator tests, net9.0 only).

**Spec:** `docs/superpowers/specs/2026-09-20-cqrs-markers-design.md`

## Global Constraints

- Every `src/` library has `IsAotCompatible=true` and warnings fail the build (`TreatWarningsAsErrors`). New types are plain interfaces: no reflection.
- Multi-targets net8.0/net9.0/net10.0 for `test/Synapse.Tests`; `test/Synapse.Generator.Tests` is net9.0 only.
- Code style: file-scoped namespaces (`namespace UnambitiousFx.Synapse.Abstractions;`), always braces, XML `<summary>` on every public type, `<typeparam>` for type parameters, comments explain why and sparingly. One public type per file.
- Tests: AAA with EXACT comments `// Arrange (Given)`, `// Act (When)`, `// Assert (Then)` each alone on its line (explanations on a separate comment line, never appended, never combined), names `Method_Scenario_ExpectedBehavior`, `[TestSubject]` where a class under test exists.
- Run tests with MTP: `dotnet test --project test/Synapse.Tests -f net10.0 --filter-class "*ClassName"` and `dotnet test --project test/Synapse.Generator.Tests -f net9.0 --filter-class "*ClassName"` (NOT `--filter`, it runs zero tests). Full run: `dotnet test --solution Synapse.slnx`.
- The four example projects treat SYN101–SYN104 as errors: `dotnet build Synapse.slnx --no-incremental` must stay at 0 warnings and no SYN1xx diagnostics.
- If a test in Task 1 or Task 2 shows that the spec's central claim is false (a constrained behavior is NOT scoped to commands by the runtime, the generator or the analyzers), STOP and report BLOCKED with the exact evidence; do not paper over it.
- Do not touch `docs/endpoints/` (untracked). Commit trailer: `Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>`.

---

### Task 1: The interfaces, shape tests and core runtime tests

**Files:**
- Create: `src/Synapse.Abstractions/ICommand.cs`, `src/Synapse.Abstractions/IQuery.cs`, `src/Synapse.Abstractions/ICommandHandler.cs`, `src/Synapse.Abstractions/IQueryHandler.cs`
- Test: `test/Synapse.Tests/Abstractions/CqrsMarkerShapeTests.cs`
- Test: `test/Synapse.Tests/Cqrs/CqrsMarkerDispatchTests.cs`

**Interfaces:**
- Produces (namespace `UnambitiousFx.Synapse.Abstractions`):
  - `public interface ICommand : IRequest;`
  - `public interface ICommand<out TResponse> : IRequest<TResponse>;`
  - `public interface IQuery<out TResponse> : IRequest<TResponse>;`
  - `public interface ICommandHandler<in TCommand> : IRequestHandler<TCommand> where TCommand : ICommand;`
  - `public interface ICommandHandler<in TCommand, TResponse> : IRequestHandler<TCommand, TResponse> where TCommand : ICommand<TResponse> where TResponse : notnull;`
  - `public interface IQueryHandler<in TQuery, TResponse> : IRequestHandler<TQuery, TResponse> where TQuery : IQuery<TResponse> where TResponse : notnull;`
- Existing API used by the tests: `IInvoker.InvokeAsync`, `cfg.RegisterRequestHandler<THandler, TRequest>()` / `<THandler, TRequest, TResponse>()`, `cfg.AddOpenGenericRequestPipelineBehavior(Type)`, `cfg.AddOpenGenericRequestWithResponsePipelineBehavior(Type)`, `IPipelineDescriber.Describe<...>()`, `provider.ValidateSynapse()`.

- [ ] **Step 1: Write the failing shape tests**

`test/Synapse.Tests/Abstractions/CqrsMarkerShapeTests.cs` (look at a neighbouring test in `test/Synapse.Tests/Abstractions/` for the exact usings and `[TestSubject]` style):
```csharp
using JetBrains.Annotations;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Tests.Abstractions;

[TestSubject(typeof(ICommand))]
public sealed class CqrsMarkerShapeTests
{
    [Fact]
    public void ICommand_IsARequest()
    {
        // Arrange (Given)
        var command = typeof(ICommand);

        // Act (When)
        var isRequest = typeof(IRequest).IsAssignableFrom(command);

        // Assert (Then)
        Assert.True(isRequest);
    }

    [Fact]
    public void ICommandOfT_IsARequestOfT()
    {
        // Arrange (Given)
        var command = typeof(ICommand<int>);

        // Act (When)
        var isRequest = typeof(IRequest<int>).IsAssignableFrom(command);

        // Assert (Then)
        Assert.True(isRequest);
    }

    [Fact]
    public void IQueryOfT_IsARequestOfT()
    {
        // Arrange (Given)
        var query = typeof(IQuery<int>);

        // Act (When)
        var isRequest = typeof(IRequest<int>).IsAssignableFrom(query);

        // Assert (Then)
        Assert.True(isRequest);
    }

    [Fact]
    public void ICommandOfT_IsNotAQuery()
    {
        // Arrange (Given)
        var command = typeof(ICommand<int>);

        // Act (When)
        var isQuery = typeof(IQuery<int>).IsAssignableFrom(command);

        // Assert (Then)
        Assert.False(isQuery);
    }

    [Fact]
    public void HandlerAliases_InheritTheMatchingRequestHandler()
    {
        // Arrange (Given)
        var voidHandler = typeof(ICommandHandler<VoidCommand>);
        var commandHandler = typeof(ICommandHandler<CreateCommand, int>);
        var queryHandler = typeof(IQueryHandler<GetQuery, int>);

        // Act (When)
        var voidInherits = typeof(IRequestHandler<VoidCommand>).IsAssignableFrom(voidHandler);
        var commandInherits = typeof(IRequestHandler<CreateCommand, int>).IsAssignableFrom(commandHandler);
        var queryInherits = typeof(IRequestHandler<GetQuery, int>).IsAssignableFrom(queryHandler);

        // Assert (Then)
        Assert.True(voidInherits);
        Assert.True(commandInherits);
        Assert.True(queryInherits);
    }

    [Fact]
    public void ICommandOfT_IsCovariantInTheResponse()
    {
        // Arrange (Given)
        ICommand<string> narrow = new NamedCommand();

        // Act (When)
        ICommand<object> wide = narrow;

        // Assert (Then)
        Assert.Same(narrow, wide);
    }

    [Fact]
    public void IQueryOfT_IsCovariantInTheResponse()
    {
        // Arrange (Given)
        IQuery<string> narrow = new NamedQuery();

        // Act (When)
        IQuery<object> wide = narrow;

        // Assert (Then)
        Assert.Same(narrow, wide);
    }

    [Fact]
    public void ICommandHandler_IsContravariantInTheCommand()
    {
        // Arrange (Given)
        ICommandHandler<BaseCommand, int> baseHandler = new BaseCommandHandler();

        // Act (When)
        ICommandHandler<DerivedCommand, int> derivedHandler = baseHandler;

        // Assert (Then)
        Assert.Same(baseHandler, derivedHandler);
    }

    private sealed record VoidCommand : ICommand;

    private sealed record CreateCommand : ICommand<int>;

    private sealed record GetQuery : IQuery<int>;

    private sealed record NamedCommand : ICommand<string>;

    private sealed record NamedQuery : IQuery<string>;

    private record BaseCommand : ICommand<int>;

    private sealed record DerivedCommand : BaseCommand;

    private sealed class BaseCommandHandler : ICommandHandler<BaseCommand, int>
    {
        public ValueTask<Result<int>> HandleAsync(BaseCommand request, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Result.Success(1));
        }
    }
}
```
`Assert.Same` on the interface-typed locals requires the same object reference (true for reference conversions). If `ValueTask.FromResult(Result.Success(1))` does not compile in this repo, copy the working form from `test/Synapse.Tests/Validation/SynapseValidatorTests.cs`.

- [ ] **Step 2: Write the failing dispatch tests**

`test/Synapse.Tests/Cqrs/CqrsMarkerDispatchTests.cs`:
```csharp
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Tests.Cqrs;

[TestSubject(typeof(ICommandHandler<,>))]
public sealed class CqrsMarkerDispatchTests
{
    [Fact]
    public async Task InvokeAsync_WithACommandHandledThroughICommandHandler_RunsTheHandler()
    {
        // Arrange (Given)
        var calls = new Calls();
        await using var provider = Build(calls, cfg => cfg.RegisterRequestHandler<CreateHandler, CreateCommand, int>());

        // Act (When)
        var result = await InvokeAsync(provider, new CreateCommand());

        // Assert (Then)
        Assert.True(result.IsSuccess);
        Assert.Equal(["CreateHandler"], calls.Items);
    }

    [Fact]
    public async Task InvokeAsync_WithAQueryHandledThroughIQueryHandler_RunsTheHandler()
    {
        // Arrange (Given)
        var calls = new Calls();
        await using var provider = Build(calls, cfg => cfg.RegisterRequestHandler<GetHandler, GetQuery, int>());

        // Act (When)
        var result = await InvokeAsync(provider, new GetQuery());

        // Assert (Then)
        Assert.True(result.IsSuccess);
        Assert.Equal(["GetHandler"], calls.Items);
    }

    [Fact]
    public async Task InvokeAsync_WithAVoidCommandHandledThroughICommandHandler_RunsTheHandler()
    {
        // Arrange (Given)
        var calls = new Calls();
        await using var provider = Build(calls, cfg => cfg.RegisterRequestHandler<SendHandler, SendCommand>());

        // Act (When)
        var result = await InvokeAsync(provider, new SendCommand());

        // Assert (Then)
        Assert.True(result.IsSuccess);
        Assert.Equal(["SendHandler"], calls.Items);
    }

    [Fact]
    public async Task InvokeAsync_WithABehaviorConstrainedToCommands_RunsItForACommandOnly()
    {
        // Arrange (Given)
        var calls = new Calls();
        await using var provider = Build(calls, cfg =>
        {
            cfg.RegisterRequestHandler<CreateHandler, CreateCommand, int>();
            cfg.RegisterRequestHandler<GetHandler, GetQuery, int>();
            cfg.AddOpenGenericRequestWithResponsePipelineBehavior(typeof(CommandOnlyBehavior<,>));
        });

        // Act (When)
        await InvokeAsync(provider, new CreateCommand());
        await InvokeAsync(provider, new GetQuery());

        // Assert (Then)
        Assert.Equal(["CommandOnlyBehavior<CreateCommand>", "CreateHandler", "GetHandler"], calls.Items);
    }

    [Fact]
    public async Task InvokeAsync_WithAVoidBehaviorConstrainedToCommands_RunsItForACommandOnly()
    {
        // Arrange (Given)
        var calls = new Calls();
        await using var provider = Build(calls, cfg =>
        {
            cfg.RegisterRequestHandler<SendHandler, SendCommand>();
            cfg.RegisterRequestHandler<PlainHandler, PlainRequest>();
            cfg.AddOpenGenericRequestPipelineBehavior(typeof(VoidCommandOnlyBehavior<>));
        });

        // Act (When)
        await InvokeAsync(provider, new SendCommand());
        await InvokeAsync(provider, new PlainRequest());

        // Assert (Then)
        Assert.Equal(["VoidCommandOnlyBehavior<SendCommand>", "SendHandler", "PlainHandler"], calls.Items);
    }

    [Fact]
    public async Task Describe_WithABehaviorConstrainedToCommands_ListsItOnTheCommandPipelineOnly()
    {
        // Arrange (Given)
        await using var provider = Build(new Calls(), cfg =>
        {
            cfg.RegisterRequestHandler<CreateHandler, CreateCommand, int>();
            cfg.RegisterRequestHandler<GetHandler, GetQuery, int>();
            cfg.AddOpenGenericRequestWithResponsePipelineBehavior(typeof(CommandOnlyBehavior<,>));
        });
        var describer = provider.GetRequiredService<IPipelineDescriber>();

        // Act (When)
        var command = describer.Describe<CreateCommand, int>();
        var query = describer.Describe<GetQuery, int>();

        // Assert (Then)
        Assert.NotNull(command);
        Assert.NotNull(query);
        Assert.Equal(typeof(CommandOnlyBehavior<CreateCommand, int>), Assert.Single(command.Behaviors).Type);
        Assert.Empty(query.Behaviors);
    }

    [Fact]
    public async Task ValidateSynapse_WithMarkerBasedHandlersAndConstrainedBehaviors_IsValidWithNoIssues()
    {
        // Arrange (Given)
        await using var provider = Build(new Calls(), cfg =>
        {
            cfg.RegisterRequestHandler<CreateHandler, CreateCommand, int>();
            cfg.RegisterRequestHandler<GetHandler, GetQuery, int>();
            cfg.RegisterRequestHandler<SendHandler, SendCommand>();
            cfg.AddOpenGenericRequestWithResponsePipelineBehavior(typeof(CommandOnlyBehavior<,>));
        });

        // Act (When)
        var report = provider.ValidateSynapse();

        // Assert (Then)
        Assert.True(report.IsValid);
        Assert.Empty(report.Issues);
    }

    private static ServiceProvider Build(Calls calls, Action<ISynapseConfig> configure)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton(calls);
        services.AddSynapse(configure);
        return services.BuildServiceProvider();
    }

    private static async Task<Result<int>> InvokeAsync<TRequest>(IServiceProvider provider, TRequest request)
        where TRequest : IRequest<int>
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IInvoker>()
            .InvokeAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<Result> InvokeAsync(IServiceProvider provider, IRequest request)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IInvoker>()
            .InvokeAsync(request, TestContext.Current.CancellationToken);
    }

    private sealed class Calls
    {
        public List<string> Items { get; } = [];
    }

    private sealed record CreateCommand : ICommand<int>;

    private sealed record GetQuery : IQuery<int>;

    private sealed record SendCommand : ICommand;

    private sealed record PlainRequest : IRequest;

    private sealed class CreateHandler(Calls calls) : ICommandHandler<CreateCommand, int>
    {
        public ValueTask<Result<int>> HandleAsync(CreateCommand request, CancellationToken cancellationToken = default)
        {
            calls.Items.Add(nameof(CreateHandler));
            return ValueTask.FromResult(Result.Success(1));
        }
    }

    private sealed class GetHandler(Calls calls) : IQueryHandler<GetQuery, int>
    {
        public ValueTask<Result<int>> HandleAsync(GetQuery request, CancellationToken cancellationToken = default)
        {
            calls.Items.Add(nameof(GetHandler));
            return ValueTask.FromResult(Result.Success(2));
        }
    }

    private sealed class SendHandler(Calls calls) : ICommandHandler<SendCommand>
    {
        public ValueTask<Result> HandleAsync(SendCommand request, CancellationToken cancellationToken = default)
        {
            calls.Items.Add(nameof(SendHandler));
            return ValueTask.FromResult(Result.Success());
        }
    }

    private sealed class PlainHandler(Calls calls) : IRequestHandler<PlainRequest>
    {
        public ValueTask<Result> HandleAsync(PlainRequest request, CancellationToken cancellationToken = default)
        {
            calls.Items.Add(nameof(PlainHandler));
            return ValueTask.FromResult(Result.Success());
        }
    }

    private sealed class CommandOnlyBehavior<TRequest, TResponse>(Calls calls)
        : IRequestPipelineBehavior<TRequest, TResponse>
        where TRequest : ICommand<TResponse>
        where TResponse : notnull
    {
        public ValueTask<Result<TResponse>> HandleAsync(TRequest request,
            RequestHandlerDelegate<TRequest, TResponse> next, CancellationToken cancellationToken = default)
        {
            calls.Items.Add($"CommandOnlyBehavior<{typeof(TRequest).Name}>");
            return next(request, cancellationToken);
        }
    }

    private sealed class VoidCommandOnlyBehavior<TRequest>(Calls calls) : IRequestPipelineBehavior<TRequest>
        where TRequest : ICommand
    {
        public ValueTask<Result> HandleAsync(TRequest request, RequestHandlerDelegate<TRequest> next,
            CancellationToken cancellationToken = default)
        {
            calls.Items.Add($"VoidCommandOnlyBehavior<{typeof(TRequest).Name}>");
            return next(request, cancellationToken);
        }
    }
}
```
Notes: (a) `IInvoker.InvokeAsync` overloads and `RequestHandlerDelegate` invocation shape may differ slightly; copy the working shapes from `test/Synapse.Tests/Validation/SynapseValidatorTests.cs` and `test/Synapse.Tests/Pipelines/OutboxDiscardOnFailureBehaviorTests.cs` (the void-invoke helper there is `Invoke`). (b) The generic `InvokeAsync<TRequest>(…) where TRequest : IRequest<int>` helper is chosen so the query and the command both infer; if overload resolution against the second (void) helper is ambiguous for a type implementing `ICommand`, rename the helpers `InvokeWithResponseAsync` / `InvokeVoidAsync`. (c) The nested behaviors are private nested types with a public primary constructor: DI activates them through their public constructor.
Tests 1–3 pass as soon as the interfaces exist. Tests 4–7 (constraint scoping, describer, validation) are the ones that PROVE the spec's claim; if 4, 5 or 6 fail because the runtime does not scope by the `ICommand<TResponse>` constraint, STOP and report BLOCKED with the failure output.

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test --project test/Synapse.Tests -f net10.0 --filter-class "*CqrsMarkerShapeTests"` → build FAIL (types missing).

- [ ] **Step 4: Implement the interfaces**

`src/Synapse.Abstractions/ICommand.cs`:
```csharp
namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Marks a request that changes state and produces no response. A pure intent marker: the library treats it like
///     any <see cref="IRequest" />, and it lets a behavior be scoped to commands with a generic constraint
///     (<c>where TRequest : ICommand</c>).
/// </summary>
public interface ICommand : IRequest;

/// <summary>
///     Marks a request that changes state and produces a response, such as the new entity's identifier. A pure intent
///     marker: the library treats it like any <see cref="IRequest{TResponse}" />, and it lets a behavior be scoped to
///     commands with a generic constraint (<c>where TRequest : ICommand&lt;TResponse&gt;</c>).
/// </summary>
/// <typeparam name="TResponse">The type of the response.</typeparam>
public interface ICommand<out TResponse> : IRequest<TResponse>;
```
`src/Synapse.Abstractions/IQuery.cs`:
```csharp
namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Marks a request that reads state and always produces data. A pure intent marker: the library treats it like any
///     <see cref="IRequest{TResponse}" />, and it lets a behavior be scoped to queries with a generic constraint
///     (<c>where TRequest : IQuery&lt;TResponse&gt;</c>).
/// </summary>
/// <typeparam name="TResponse">The type of the data returned.</typeparam>
public interface IQuery<out TResponse> : IRequest<TResponse>;
```
`src/Synapse.Abstractions/ICommandHandler.cs`:
```csharp
namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Handles a <see cref="ICommand" />. A naming alias of <see cref="IRequestHandler{TRequest}" />: registration,
///     the source generator and the analyzers treat it exactly like any request handler.
/// </summary>
/// <typeparam name="TCommand">The command type.</typeparam>
public interface ICommandHandler<in TCommand> : IRequestHandler<TCommand>
    where TCommand : ICommand;

/// <summary>
///     Handles a <see cref="ICommand{TResponse}" />. A naming alias of
///     <see cref="IRequestHandler{TRequest, TResponse}" />: registration, the source generator and the analyzers treat
///     it exactly like any request handler.
/// </summary>
/// <typeparam name="TCommand">The command type.</typeparam>
/// <typeparam name="TResponse">The type of the response.</typeparam>
public interface ICommandHandler<in TCommand, TResponse> : IRequestHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
    where TResponse : notnull;
```
`src/Synapse.Abstractions/IQueryHandler.cs`:
```csharp
namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Handles an <see cref="IQuery{TResponse}" />. A naming alias of <see cref="IRequestHandler{TRequest, TResponse}" />:
///     registration, the source generator and the analyzers treat it exactly like any request handler.
/// </summary>
/// <typeparam name="TQuery">The query type.</typeparam>
/// <typeparam name="TResponse">The type of the data returned.</typeparam>
public interface IQueryHandler<in TQuery, TResponse> : IRequestHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>
    where TResponse : notnull;
```
If two public types per file is disallowed by a repo analyzer, split `ICommandHandler` into `ICommandHandler.cs` (void) and `ICommandHandlerOfT.cs`— follow how `IRequestHandler.cs` (two interfaces in one file) is done: keeping both in one file is consistent with the repo.

- [ ] **Step 5: Run to verify pass**

Run both classes: `--filter-class "*CqrsMarkerShapeTests"` (8 pass) and `--filter-class "*CqrsMarkerDispatchTests"` (7 pass). Mutation check: temporarily change `where TRequest : ICommand<TResponse>` on `CommandOnlyBehavior` to `where TRequest : IRequest<TResponse>` in the test file and confirm `InvokeAsync_WithABehaviorConstrainedToCommands_RunsItForACommandOnly` and the describer test FAIL; revert. Then `dotnet build Synapse.slnx` (0 warnings) and `dotnet test --solution Synapse.slnx`.

- [ ] **Step 6: Commit**

```bash
git add src/Synapse.Abstractions test/Synapse.Tests
git commit -m "feat(abstractions): add ICommand, IQuery and handler aliases"
```

---

### Task 2: Generator and analyzer coverage

**Files:**
- Test: `test/Synapse.Generator.Tests/CqrsMarkerGeneratorTests.cs`
- Test: `test/Synapse.Generator.Tests/Analyzers/CqrsMarkerAnalyzerTests.cs`

**Interfaces:**
- Consumes: the Task 1 interfaces; `SynapseGenerator`; `RequestWithoutHandlerAnalyzer`, `UnattributedHandlerAnalyzer`, `BehaviorWithoutHandlersAnalyzer`; `AnalyzerTestHelper` (`RunAsync<TAnalyzer>(string)`, `Preamble`) in `UnambitiousFx.Synapse.Generator.Tests.Analyzers`.
- Produces: tests only. If the generator or an analyzer mishandles the markers, fix it ONLY if the fix is a small correction inside `src/Synapse.Generator` with the failing test as proof; otherwise STOP and report BLOCKED with the evidence.

- [ ] **Step 1: Write the generator tests**

`test/Synapse.Generator.Tests/CqrsMarkerGeneratorTests.cs`:
```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Generator;

namespace UnambitiousFx.Synapse.Generator.Tests;

/// <summary>
///     The source generator must treat ICommand / IQuery requests and their handler aliases like any request, and
///     scope a behavior by its generic constraint.
/// </summary>
public sealed class CqrsMarkerGeneratorTests
{
    private const string Usings = """
        using System.Threading;
        using System.Threading.Tasks;
        using UnambitiousFx.Functional;
        using UnambitiousFx.Synapse.Abstractions;

        namespace TestNs;

        """;

    private const string Types = """
        public sealed record CreateCommand : ICommand<int>;

        public sealed record GetQuery : IQuery<int>;

        [RequestHandler<CreateCommand, int>]
        public sealed class CreateHandler : ICommandHandler<CreateCommand, int>
        {
            public ValueTask<Result<int>> HandleAsync(CreateCommand request, CancellationToken ct = default)
                => ValueTask.FromResult(Result.Success(1));
        }

        [RequestHandler<GetQuery, int>]
        public sealed class GetHandler : IQueryHandler<GetQuery, int>
        {
            public ValueTask<Result<int>> HandleAsync(GetQuery request, CancellationToken ct = default)
                => ValueTask.FromResult(Result.Success(2));
        }

        public sealed class TransactionBehavior<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
            where TRequest : ICommand<TResponse>
            where TResponse : notnull
        {
            public ValueTask<Result<TResponse>> HandleAsync(TRequest request,
                RequestHandlerDelegate<TRequest, TResponse> next, CancellationToken ct = default)
                => next(request, ct);
        }
        """;

    [Fact]
    public void Generate_WithHandlersImplementingTheAliases_EmitsTheirRegistrationsAndCompiles()
    {
        // Arrange (Given)
        var source = Usings + Types;

        // Act (When)
        var (generated, errors) = Run(source);

        // Assert (Then)
        Assert.Contains("RegisterRequestHandler<global::TestNs.CreateHandler, global::TestNs.CreateCommand, int>()",
            generated);
        Assert.Contains("RegisterRequestHandler<global::TestNs.GetHandler, global::TestNs.GetQuery, int>()",
            generated);
        Assert.Empty(errors);
    }

    [Fact]
    public void Generate_WithAPipelineBehaviorConstrainedToCommands_ClosesItOverCommandHandlersOnly()
    {
        // Arrange (Given)
        var source = Usings + Types.Replace("public sealed class TransactionBehavior",
            "[PipelineBehavior]\npublic sealed class TransactionBehavior");

        // Act (When)
        var (generated, errors) = Run(source);

        // Assert (Then)
        Assert.Contains("TransactionBehavior<global::TestNs.CreateCommand, int>", generated);
        Assert.DoesNotContain("TransactionBehavior<global::TestNs.GetQuery", generated);
        Assert.Empty(errors);
    }

    [Fact]
    public void Generate_WithAGlobalBehaviorConstrainedToCommands_ClosesItOverCommandHandlersOnly()
    {
        // Arrange (Given)
        // A using directive must precede the assembly attribute.
        var source = Usings.Replace("namespace TestNs;\n\n", string.Empty)
                     + "[assembly: SynapseGlobalBehavior(typeof(TestNs.TransactionBehavior<,>))]\n\nnamespace TestNs;\n\n"
                     + Types;

        // Act (When)
        var (generated, errors) = Run(source);

        // Assert (Then)
        Assert.Contains("TransactionBehavior<global::TestNs.CreateCommand, int>", generated);
        Assert.DoesNotContain("TransactionBehavior<global::TestNs.GetQuery", generated);
        Assert.Empty(errors);
    }

    private static (string Generated, ImmutableArray<Diagnostic> Errors) Run(string source)
    {
        var compilation = CSharpCompilation.Create("TestAssembly", [CSharpSyntaxTree.ParseText(source)],
            GetMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var driver = CSharpGeneratorDriver.Create(new SynapseGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);

        var generated = driver.GetRunResult().GeneratedTrees
            .FirstOrDefault(tree => tree.FilePath.EndsWith("RegisterGroup.g.cs", StringComparison.Ordinal))?
            .GetText().ToString() ?? string.Empty;
        var errors = updated.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToImmutableArray();
        return (generated, errors);
    }

    private static IEnumerable<MetadataReference> GetMetadataReferences()
    {
        // Same references as GeneratorBehaviorTests.GetMetadataReferences (bottom of that file).
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
Implementer notes: (a) print `generated` once while writing the tests and assert on stable substrings of what the generator really emits (the exact `RegisterRequestHandler<...>` / behavior spelling above is the expected form; adapt the literal if the generator formats differently, but keep the meaning: command behavior present for `CreateCommand`, absent for `GetQuery`). (b) The tests must NOT use `\n`-dependent `Replace` on multi-line raw strings that could be CRLF on Windows: only single-line anchors such as `"public sealed class TransactionBehavior"` are used; keep it that way (a previous PR failed on Windows for exactly this). The third test builds the source by concatenation, not by replacing across lines: rewrite `Usings.Replace("namespace TestNs;\n\n", ...)` to avoid the multi-line replace, for example by defining a separate `UsingsOnly` constant (the four `using` lines) and composing `UsingsOnly + assembly attribute + "namespace TestNs;\n" + Types`. (c) If `Assert.Empty(errors)` fails, the generated code does not compile with the alias interfaces: that is a real finding; report it with the diagnostics.

- [ ] **Step 2: Write the analyzer tests**

`test/Synapse.Generator.Tests/Analyzers/CqrsMarkerAnalyzerTests.cs`:
```csharp
using JetBrains.Annotations;
using Microsoft.CodeAnalysis;
using UnambitiousFx.Synapse.Generator.Analyzers;

namespace UnambitiousFx.Synapse.Generator.Tests.Analyzers;

/// <summary>
///     The SYN analyzers must recognize handlers written against the CQRS aliases and understand marker constraints.
/// </summary>
[TestSubject(typeof(RequestWithoutHandlerAnalyzer))]
public sealed class CqrsMarkerAnalyzerTests
{
    [Fact]
    public async Task Syn101_WithACommandHandledThroughICommandHandler_ReportsNothing()
    {
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + """
            public sealed record CreateCommand : ICommand<int>;

            public sealed class CreateHandler : ICommandHandler<CreateCommand, int>
            {
                public ValueTask<Result<int>> HandleAsync(CreateCommand request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success(1));
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<RequestWithoutHandlerAnalyzer>(source);

        // Assert (Then)
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Syn101_WithAQueryHandledThroughIQueryHandler_ReportsNothing()
    {
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + """
            public sealed record GetQuery : IQuery<int>;

            public sealed class GetHandler : IQueryHandler<GetQuery, int>
            {
                public ValueTask<Result<int>> HandleAsync(GetQuery request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success(1));
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<RequestWithoutHandlerAnalyzer>(source);

        // Assert (Then)
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Syn101_WithACommandAndNoHandler_ReportsSyn101()
    {
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + "public sealed record SendCommand : ICommand;";

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<RequestWithoutHandlerAnalyzer>(source);

        // Assert (Then)
        Assert.Equal("SYN101", Assert.Single(diagnostics).Id);
    }

    [Fact]
    public async Task Syn104_WithAnUnattributedQueryHandlerNextToAnAttributedOne_ReportsSyn104()
    {
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + """
            public sealed record CreateCommand : ICommand<int>;

            public sealed record GetQuery : IQuery<int>;

            [RequestHandler<CreateCommand, int>]
            public sealed class CreateHandler : ICommandHandler<CreateCommand, int>
            {
                public ValueTask<Result<int>> HandleAsync(CreateCommand request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success(1));
            }

            public sealed class GetHandler : IQueryHandler<GetQuery, int>
            {
                public ValueTask<Result<int>> HandleAsync(GetQuery request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success(1));
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<UnattributedHandlerAnalyzer>(source);

        // Assert (Then)
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("SYN104", diagnostic.Id);
        Assert.Contains("GetHandler", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Syn102_WithACommandOnlyBehaviorAndACommandHandler_ReportsNothing()
    {
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + """
            public sealed record SendCommand : ICommand;

            [RequestHandler<SendCommand>]
            public sealed class SendHandler : ICommandHandler<SendCommand>
            {
                public ValueTask<Result> HandleAsync(SendCommand request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success());
            }

            [PipelineBehavior]
            public sealed class AuditBehavior<TRequest> : IRequestPipelineBehavior<TRequest>
                where TRequest : ICommand
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
    public async Task Syn102_WithACommandOnlyBehaviorAndOnlyAPlainRequestHandler_ReportsSyn102()
    {
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + """
            public sealed record PlainRequest : IRequest;

            [RequestHandler<PlainRequest>]
            public sealed class PlainHandler : IRequestHandler<PlainRequest>
            {
                public ValueTask<Result> HandleAsync(PlainRequest request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success());
            }

            [PipelineBehavior]
            public sealed class AuditBehavior<TRequest> : IRequestPipelineBehavior<TRequest>
                where TRequest : ICommand
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
}
```
The last test proves the analyzer understands the marker constraint (a plain `IRequest` handler does not satisfy `ICommand`). If `AnalyzerTestHelper.RunAsync` asserts fixtures compile (it does since the analyzers PR), all fixtures above must compile.

- [ ] **Step 3: Run**

Run: `dotnet test --project test/Synapse.Generator.Tests -f net9.0 --filter-class "*CqrsMarkerGeneratorTests"` (3) and `--filter-class "*CqrsMarkerAnalyzerTests"` (6). These test existing behavior, so they may pass immediately; that is expected. To satisfy "watch it fail": make one deliberate mutation per class (in the generator test, assert `DoesNotContain(...CreateCommand...)`; in the analyzer test, change the expected id) and confirm it fails, then revert. Record both in your report.

- [ ] **Step 4: Full verification and commit**

`dotnet build Synapse.slnx --no-incremental` (0 warnings, no SYN1xx), `dotnet test --solution Synapse.slnx` (all green).
```bash
git add test/Synapse.Generator.Tests
git commit -m "test(generator): cover ICommand/IQuery markers in the generator and analyzers"
```

---

### Task 3: Docs and final verification

**Files:**
- Modify: `docs/docs/commands-and-queries.mdx`
- Modify: `docs/docs/pipelines.mdx`

**Interfaces:**
- Consumes: the Task 1 types; the existing docs sections "CQRS boundary enforcement" (`pipelines.mdx`), "Applying a behavior to every handler".

- [ ] **Step 1: commands-and-queries.mdx**

Replace the intro sentence and table so they say commands and queries are both requests, and that the optional intent markers are `ICommand` / `ICommand<TResponse>` / `IQuery<TResponse>` (in `UnambitiousFx.Synapse.Abstractions`), pure markers the library treats like any request. Update the table's marker column: Command: `ICommand` or `ICommand<TResponse>` (still an `IRequest` underneath); Query: `IQuery<TResponse>`. Add a short subsection "Marker interfaces and handler aliases" with one example:
```csharp
public record CreateTaskCommand(string Title) : ICommand<Guid>;
public record GetTaskQuery(Guid TaskId) : IQuery<TaskDto>;

[RequestHandler<CreateTaskCommand, Guid>]
public class CreateTaskCommandHandler : ICommandHandler<CreateTaskCommand, Guid> { ... }
```
State: `ICommandHandler<>`, `ICommandHandler<,>` and `IQueryHandler<,>` are naming aliases of `IRequestHandler`; registration still uses `[RequestHandler<...>]`; no marker is required; existing `IRequest` code keeps working. Keep the link to the CQRS enforcement section and say it does not use the markers (it still blocks any request inside a handler).

- [ ] **Step 2: pipelines.mdx**

Add `## Applying a behavior to commands or queries only` after "Applying a behavior to every handler" (before "Built-in behaviors"): a behavior constrained with `where TRequest : ICommand<TResponse>` (and `where TResponse : notnull`) applies only to commands; show the `[PipelineBehavior]` snippet for a `TransactionBehavior<TRequest, TResponse>`; state that the same works with `[assembly: SynapseGlobalBehavior]` (source generator, Native-AOT safe) and with the runtime `AddOpenGenericRequestWithResponsePipelineBehavior(typeof(TransactionBehavior<,>))` (DI skips closings whose constraints do not hold; that path is not Native-AOT safe for value-type responses, so prefer `[PipelineBehavior]` under AOT); note the void variant `where TRequest : ICommand` for `IRequestPipelineBehavior<TRequest>`; note there are no dedicated `AddCommandPipelineBehavior` helpers: the constraint is the scoping mechanism. All generics inside backticks or code fences (MDX). Then `cd docs && pnpm build` → success, zero broken-link warnings (run `pnpm install` first only if `node_modules` is missing).

- [ ] **Step 3: Full verification**

`dotnet build Synapse.slnx --no-incremental` (0 warnings, no SYN1xx), `dotnet test --solution Synapse.slnx` (all green), `git status` shows only intended files (never `docs/endpoints/`).

- [ ] **Step 4: Commit**

```bash
git add docs/docs
git commit -m "docs: document ICommand/IQuery markers and constraint-scoped behaviors"
```
