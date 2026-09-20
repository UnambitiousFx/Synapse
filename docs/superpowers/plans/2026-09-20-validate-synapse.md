# ValidateSynapse / ValidateOnStart Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `IServiceProvider.ValidateSynapse()` (report) and `cfg.ValidateOnStart()` (fail host start) that detect behavior-without-handler, duplicate handlers, unresolvable pipelines and `Order` ties.

**Architecture:** Registration records closed-generic "probes" (`Func<IPipelineDescriber, PipelineDescription?>`) per handler/event, and `Apply` snapshots handler/behavior `ServiceDescriptor`s into an internal singleton `SynapseRegistry`. An internal `SynapseValidator` runs four checks over the registry using `IPipelineDescriber`; a hosted service runs it at startup. Nothing uses reflection over assemblies or `MakeGenericType`, so it is Native-AOT safe.

**Tech Stack:** .NET 8/9/10 multi-target, xUnit v3 on Microsoft Testing Platform, NSubstitute, `Microsoft.Extensions.Hosting/Logging`.

**Spec:** `docs/superpowers/specs/2026-09-20-validate-synapse-design.md`

## Global Constraints

- Every `src/` library has `IsAotCompatible=true` and warnings fail the build: no `MakeGenericType`/`MakeGenericMethod`/assembly scanning; no new trim/AOT warnings.
- Multi-targets net8.0/net9.0/net10.0; build with `dotnet build Synapse.slnx`.
- Code style: file-scoped namespaces, always braces, XML doc (`<summary>`/`<param>`/`<returns>`) on public APIs, `_camelCase` private fields, comments explain why, sparingly.
- Tests: AAA with `// Arrange (Given)` `// Act (When)` `// Assert (Then)` comments, names `Method_Scenario_ExpectedBehavior`, `[TestSubject(typeof(...))]` from JetBrains.Annotations, `[Fact]`/`[Theory]`.
- Run tests with MTP: `dotnet test --project test/Synapse.Tests -f net10.0 --filter-class "*ClassName"` (NOT `--filter`, it runs zero tests). Full run: `dotnet test --solution Synapse.slnx`.
- Codes are stable: SYN001 error, SYN002 error, SYN003 error, SYN004 warning.
- Commit trailer: `Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>`.
- Do not touch `docs/endpoints/` (untracked, not ours).

---

### Task 1: Report types

**Files:**
- Create: `src/Synapse.Abstractions/SynapseValidationSeverity.cs`
- Create: `src/Synapse.Abstractions/SynapseValidationIssue.cs`
- Create: `src/Synapse.Abstractions/SynapseValidationReport.cs`
- Create: `src/Synapse.Abstractions/SynapseValidationException.cs`
- Test: `test/Synapse.Tests/Validation/SynapseValidationReportTests.cs`

**Interfaces:**
- Produces (namespace `UnambitiousFx.Synapse.Abstractions`):
  - `enum SynapseValidationSeverity { Warning, Error }`
  - `sealed record SynapseValidationIssue(string Code, SynapseValidationSeverity Severity, string Message, Type Type)`
  - `sealed class SynapseValidationReport` with public ctor `(IEnumerable<SynapseValidationIssue> issues)`, `Issues`, `Errors`, `Warnings` (`IReadOnlyList<SynapseValidationIssue>`), `bool IsValid`, `void ThrowIfInvalid()`, `ToString()`.
  - `sealed class SynapseValidationException : Exception` with `SynapseValidationReport Report`, ctor `(SynapseValidationReport report)`.

- [ ] **Step 1: Write the failing tests**

```csharp
using JetBrains.Annotations;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Tests.Validation;

[TestSubject(typeof(SynapseValidationReport))]
public sealed class SynapseValidationReportTests
{
    private static SynapseValidationIssue Error(string code = "SYN001") =>
        new(code, SynapseValidationSeverity.Error, "an error", typeof(string));

    private static SynapseValidationIssue Warning(string code = "SYN004") =>
        new(code, SynapseValidationSeverity.Warning, "a warning", typeof(int));

    [Fact]
    public void IsValid_WithNoIssues_IsTrue()
    {
        // Arrange (Given)
        var report = new SynapseValidationReport([]);

        // Act (When)
        var isValid = report.IsValid;

        // Assert (Then)
        Assert.True(isValid);
        Assert.Empty(report.Issues);
    }

    [Fact]
    public void IsValid_WithOnlyWarnings_IsTrueAndThrowIfInvalidDoesNotThrow()
    {
        // Arrange (Given)
        var report = new SynapseValidationReport([Warning()]);

        // Act (When)
        report.ThrowIfInvalid();

        // Assert (Then)
        Assert.True(report.IsValid);
        Assert.Single(report.Warnings);
        Assert.Empty(report.Errors);
    }

    [Fact]
    public void IsValid_WithAnError_IsFalseAndSplitsErrorsFromWarnings()
    {
        // Arrange (Given)
        var report = new SynapseValidationReport([Warning(), Error()]);

        // Act (When)
        var isValid = report.IsValid;

        // Assert (Then)
        Assert.False(isValid);
        Assert.Equal("SYN001", Assert.Single(report.Errors).Code);
        Assert.Equal("SYN004", Assert.Single(report.Warnings).Code);
        Assert.Equal(2, report.Issues.Count);
    }

    [Fact]
    public void ThrowIfInvalid_WithErrors_ThrowsWithTheReportAndListsEveryError()
    {
        // Arrange (Given)
        var report = new SynapseValidationReport([Error("SYN001"), Error("SYN002"), Warning()]);

        // Act (When)
        var exception = Assert.Throws<SynapseValidationException>(report.ThrowIfInvalid);

        // Assert (Then)
        Assert.Same(report, exception.Report);
        Assert.Contains("SYN001", exception.Message);
        Assert.Contains("SYN002", exception.Message);
        Assert.DoesNotContain("SYN004", exception.Message);
    }

    [Fact]
    public void Constructor_WithNullIssues_Throws()
    {
        // Arrange (Given) / Act (When) / Assert (Then)
        Assert.Throws<ArgumentNullException>(() => new SynapseValidationReport(null!));
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test --project test/Synapse.Tests -f net10.0 --filter-class "*SynapseValidationReportTests"`
Expected: build FAIL (types missing).

- [ ] **Step 3: Implement**

`SynapseValidationSeverity.cs`:
```csharp
namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     How serious a <see cref="SynapseValidationIssue" /> is.
/// </summary>
public enum SynapseValidationSeverity
{
    /// <summary>Legal but probably unintended; does not make a report invalid.</summary>
    Warning,

    /// <summary>A misconfiguration that makes the report invalid.</summary>
    Error
}
```

`SynapseValidationIssue.cs`:
```csharp
namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     One finding of <c>ValidateSynapse</c>.
/// </summary>
/// <param name="Code">A stable identifier such as <c>SYN001</c>.</param>
/// <param name="Severity">Whether the finding is an error or a warning.</param>
/// <param name="Message">What is wrong and how to fix it.</param>
/// <param name="Type">The request or event type the finding is about.</param>
public sealed record SynapseValidationIssue(
    string Code,
    SynapseValidationSeverity Severity,
    string Message,
    Type Type);
```

`SynapseValidationReport.cs`:
```csharp
using System.Text;

namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     The outcome of <c>ValidateSynapse</c>. Only errors make it invalid; warnings do not.
/// </summary>
public sealed class SynapseValidationReport
{
    /// <summary>
    ///     Creates a report from a list of findings.
    /// </summary>
    /// <param name="issues">The findings.</param>
    /// <exception cref="ArgumentNullException"><paramref name="issues" /> is <c>null</c>.</exception>
    public SynapseValidationReport(IEnumerable<SynapseValidationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        Issues = issues.ToArray();
        Errors = Issues.Where(issue => issue.Severity == SynapseValidationSeverity.Error).ToArray();
        Warnings = Issues.Where(issue => issue.Severity == SynapseValidationSeverity.Warning).ToArray();
    }

    /// <summary>Every finding.</summary>
    public IReadOnlyList<SynapseValidationIssue> Issues { get; }

    /// <summary>The findings that make the report invalid.</summary>
    public IReadOnlyList<SynapseValidationIssue> Errors { get; }

    /// <summary>The findings that are legal but suspicious.</summary>
    public IReadOnlyList<SynapseValidationIssue> Warnings { get; }

    /// <summary><c>true</c> when there are no errors. Warnings do not count.</summary>
    public bool IsValid => Errors.Count == 0;

    /// <summary>
    ///     Throws when the report has errors.
    /// </summary>
    /// <exception cref="SynapseValidationException">The report has at least one error.</exception>
    public void ThrowIfInvalid()
    {
        if (!IsValid)
        {
            throw new SynapseValidationException(this);
        }
    }

    /// <summary>
    ///     Lists every finding, one per line.
    /// </summary>
    /// <returns>The findings as text.</returns>
    public override string ToString()
    {
        var builder = new StringBuilder();
        foreach (var issue in Issues)
        {
            builder.Append(issue.Severity).Append(' ').Append(issue.Code).Append(": ").AppendLine(issue.Message);
        }

        return builder.ToString();
    }
}
```

`SynapseValidationException.cs`:
```csharp
using System.Text;

namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Thrown when the Synapse configuration is invalid.
/// </summary>
public sealed class SynapseValidationException : Exception
{
    /// <summary>
    ///     Creates the exception from an invalid report.
    /// </summary>
    /// <param name="report">The report; its errors form the message.</param>
    public SynapseValidationException(SynapseValidationReport report)
        : base(BuildMessage(report))
    {
        Report = report;
    }

    /// <summary>The full report, warnings included.</summary>
    public SynapseValidationReport Report { get; }

    private static string BuildMessage(SynapseValidationReport report)
    {
        var builder = new StringBuilder("The Synapse configuration is invalid:");
        foreach (var error in report.Errors)
        {
            builder.AppendLine().Append(error.Code).Append(": ").Append(error.Message);
        }

        return builder.ToString();
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test --project test/Synapse.Tests -f net10.0 --filter-class "*SynapseValidationReportTests"`
Expected: 5 passed.

- [ ] **Step 5: Commit**

```bash
git add src/Synapse.Abstractions/SynapseValidation*.cs test/Synapse.Tests/Validation
git commit -m "feat(validation): add SynapseValidationReport, issue and exception types"
```

---

### Task 2: Registry (probes and descriptor snapshot)

**Files:**
- Create: `src/Synapse/Validation/SynapseRegistry.cs`
- Modify: `src/Synapse/SynapseConfig.cs` (six registration sites + `AddRegisterGroup` + `Apply`)
- Modify: `src/Synapse/DefaultDependencyInjectionBuilder.cs` (six registration sites + `Probes` property)
- Test: `test/Synapse.Tests/Validation/SynapseRegistryTests.cs`

**Interfaces:**
- Consumes: `IPipelineDescriber`, `PipelineDescription` (Abstractions).
- Produces (`internal sealed class SynapseRegistry`, namespace `UnambitiousFx.Synapse.Validation`):
  - `IReadOnlyDictionary<Type, Func<IPipelineDescriber, PipelineDescription?>> Probes` — key = request or event type.
  - `IReadOnlyDictionary<Type, int> RequestHandlerCounts` — request type → number of `IRequestHandler<>`/`<,>` descriptors.
  - `IReadOnlySet<Type> EventsWithHandlers`, `IReadOnlySet<Type> RequestsWithBehaviors`, `IReadOnlySet<Type> EventsWithBehaviors`.
  - `static SynapseRegistry Create(IServiceCollection services, IReadOnlyDictionary<Type, Func<IPipelineDescriber, PipelineDescription?>> probes)`.
- `DefaultDependencyInjectionBuilder.Probes` : `IReadOnlyDictionary<Type, Func<IPipelineDescriber, PipelineDescription?>>`.
- `SynapseRegistry` is resolvable from the provider as a singleton after `AddSynapse`.

- [ ] **Step 1: Write the failing tests**

Read `test/Synapse.Tests/Pipelines/PipelineDescriberTests.cs` first for how `Build` wires `AddSynapse`, and mirror it. Tests use private nested request/handler types.

```csharp
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Validation;

namespace UnambitiousFx.Synapse.Tests.Validation;

[TestSubject(typeof(SynapseRegistry))]
public sealed class SynapseRegistryTests
{
    [Fact]
    public void Create_WithHandlersRegisteredDirectly_RecordsAProbePerRequestAndEvent()
    {
        // Arrange (Given)
        using var provider = Build(cfg =>
        {
            cfg.RegisterRequestHandler<PlainHandler, PlainCommand>();
            cfg.RegisterRequestHandler<CountHandler, CountQuery, int>();
            cfg.RegisterEventHandler<PingHandler, PingEvent>();
        });

        // Act (When)
        var registry = provider.GetRequiredService<SynapseRegistry>();

        // Assert (Then)
        Assert.Contains(typeof(PlainCommand), registry.Probes.Keys);
        Assert.Contains(typeof(CountQuery), registry.Probes.Keys);
        Assert.Contains(typeof(PingEvent), registry.Probes.Keys);
        Assert.Equal(1, registry.RequestHandlerCounts[typeof(PlainCommand)]);
        Assert.Equal(1, registry.RequestHandlerCounts[typeof(CountQuery)]);
        Assert.Contains(typeof(PingEvent), registry.EventsWithHandlers);
    }

    [Fact]
    public void Create_WithHandlersRegisteredThroughAGroup_RecordsTheirProbes()
    {
        // Arrange (Given)
        using var provider = Build(cfg => cfg.AddRegisterGroup(new TestGroup()));

        // Act (When)
        var registry = provider.GetRequiredService<SynapseRegistry>();

        // Assert (Then)
        Assert.Contains(typeof(PlainCommand), registry.Probes.Keys);
        Assert.Contains(typeof(PingEvent), registry.Probes.Keys);
    }

    [Fact]
    public void Create_WithAConditionalHandlerWhoseConditionIsFalse_RecordsNoProbe()
    {
        // Arrange (Given)
        using var provider = Build(cfg => cfg.RegisterRequestHandlerWhen<PlainHandler, PlainCommand>(() => false));

        // Act (When)
        var registry = provider.GetRequiredService<SynapseRegistry>();

        // Assert (Then)
        Assert.DoesNotContain(typeof(PlainCommand), registry.Probes.Keys);
        Assert.DoesNotContain(typeof(PlainCommand), registry.RequestHandlerCounts.Keys);
    }

    [Fact]
    public void Create_WithTwoHandlersForOneRequest_CountsBoth()
    {
        // Arrange (Given)
        using var provider = Build(cfg =>
        {
            cfg.RegisterRequestHandler<PlainHandler, PlainCommand>();
            cfg.RegisterRequestHandler<OtherPlainHandler, PlainCommand>();
        });

        // Act (When)
        var registry = provider.GetRequiredService<SynapseRegistry>();

        // Assert (Then)
        Assert.Equal(2, registry.RequestHandlerCounts[typeof(PlainCommand)]);
    }

    [Fact]
    public void Create_WithClosedBehaviors_RecordsTheTypesTheyTarget()
    {
        // Arrange (Given)
        using var provider = Build(cfg =>
        {
            cfg.RegisterRequestPipelineBehavior<NoopBehavior<PlainCommand>, PlainCommand>();
            cfg.RegisterRequestPipelineBehavior<NoopResponseBehavior, CountQuery, int>();
            cfg.RegisterEventPipelineBehavior<NoopEventBehavior, PingEvent>();
        });

        // Act (When)
        var registry = provider.GetRequiredService<SynapseRegistry>();

        // Assert (Then)
        Assert.Contains(typeof(PlainCommand), registry.RequestsWithBehaviors);
        Assert.Contains(typeof(CountQuery), registry.RequestsWithBehaviors);
        Assert.Contains(typeof(PingEvent), registry.EventsWithBehaviors);
    }

    [Fact]
    public void Create_WithAnOpenGenericBehavior_IgnoresIt()
    {
        // Arrange (Given) — DI closes open generics lazily over any request, so they target nothing in particular
        using var provider = Build(cfg =>
        {
            cfg.RegisterRequestHandler<PlainHandler, PlainCommand>();
            cfg.AddOpenGenericRequestPipelineBehavior(typeof(NoopBehavior<>));
        });

        // Act (When)
        var registry = provider.GetRequiredService<SynapseRegistry>();

        // Assert (Then)
        Assert.Empty(registry.RequestsWithBehaviors);
    }

    [Fact]
    public async Task Probe_WhenInvoked_DescribesTheRegisteredPipeline()
    {
        // Arrange (Given)
        await using var provider = Build(cfg =>
        {
            cfg.RegisterRequestHandler<PlainHandler, PlainCommand>();
            cfg.RegisterRequestPipelineBehavior<NoopBehavior<PlainCommand>, PlainCommand>();
        });
        var registry = provider.GetRequiredService<SynapseRegistry>();

        // Act (When)
        var description = registry.Probes[typeof(PlainCommand)](provider.GetRequiredService<IPipelineDescriber>());

        // Assert (Then)
        Assert.NotNull(description);
        Assert.Equal(typeof(PlainHandler), Assert.Single(description.Handlers));
        Assert.Equal(typeof(NoopBehavior<PlainCommand>), Assert.Single(description.Behaviors).Type);
    }

    private static ServiceProvider Build(Action<ISynapseConfig> configure)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSynapse(configure);
        return services.BuildServiceProvider();
    }

    private sealed record PlainCommand : IRequest;

    private sealed record CountQuery : IRequest<int>;

    private sealed record PingEvent : IEvent;

    private sealed class PlainHandler : IRequestHandler<PlainCommand>
    {
        public ValueTask<Result> HandleAsync(PlainCommand request, CancellationToken cancellationToken = default) =>
            new(Result.Success());
    }

    private sealed class OtherPlainHandler : IRequestHandler<PlainCommand>
    {
        public ValueTask<Result> HandleAsync(PlainCommand request, CancellationToken cancellationToken = default) =>
            new(Result.Success());
    }

    private sealed class CountHandler : IRequestHandler<CountQuery, int>
    {
        public ValueTask<Result<int>> HandleAsync(CountQuery request, CancellationToken cancellationToken = default) =>
            new(Result.Success(1));
    }

    private sealed class PingHandler : IEventHandler<PingEvent>
    {
        public ValueTask<Result> HandleAsync(PingEvent @event, CancellationToken cancellationToken = default) =>
            new(Result.Success());
    }

    private sealed class NoopBehavior<TRequest> : IRequestPipelineBehavior<TRequest>
        where TRequest : IRequest
    {
        public ValueTask<Result> HandleAsync(TRequest request, RequestHandlerDelegate<TRequest> next,
            CancellationToken cancellationToken = default) => next(request, cancellationToken);
    }

    private sealed class NoopResponseBehavior : IRequestPipelineBehavior<CountQuery, int>
    {
        public ValueTask<Result<int>> HandleAsync(CountQuery request, RequestHandlerDelegate<CountQuery, int> next,
            CancellationToken cancellationToken = default) => next(request, cancellationToken);
    }

    private sealed class NoopEventBehavior : IEventPipelineBehavior<PingEvent>
    {
        public ValueTask<Result> HandleAsync(PingEvent @event, EventHandlerDelegate<PingEvent> next,
            CancellationToken cancellationToken = default) => next(@event, cancellationToken);
    }

    private sealed class TestGroup : IRegisterGroup
    {
        public void Register(IDependencyInjectionBuilder builder)
        {
            builder.RegisterRequestHandler<PlainHandler, PlainCommand>();
            builder.RegisterEventHandler<PingHandler, PingEvent>();
        }
    }
}
```

If a helper signature above does not compile (for example `Result.Success(1)`, the delegate `next(request, cancellationToken)` shape, or `IEvent` members), copy the working form from `PipelineDescriberTests.cs` — that file already compiles the same kinds of nested types.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test --project test/Synapse.Tests -f net10.0 --filter-class "*SynapseRegistryTests"`
Expected: build FAIL (`SynapseRegistry` missing).

- [ ] **Step 3: Implement the registry**

`src/Synapse/Validation/SynapseRegistry.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Validation;

/// <summary>
///     What <c>AddSynapse</c> registered, captured for <c>ValidateSynapse</c>.
/// </summary>
/// <remarks>
///     Handler and behavior facts come from a snapshot of the <see cref="ServiceDescriptor" />s taken when
///     <c>AddSynapse</c> finishes, so a behavior added to the collection afterwards is not seen. Only descriptors
///     whose service type is an already-closed generic are read, through <see cref="Type.GetGenericArguments" />;
///     nothing is constructed by reflection, which keeps this Native-AOT safe. Open-generic behavior descriptors are
///     skipped: DI closes them lazily over any request, so which requests they apply to cannot be known here.
///     Probes are recorded by the closed <c>Register…Handler&lt;…&gt;</c> methods for the same reason.
/// </remarks>
internal sealed class SynapseRegistry
{
    private SynapseRegistry(
        IReadOnlyDictionary<Type, Func<IPipelineDescriber, PipelineDescription?>> probes,
        IReadOnlyDictionary<Type, int> requestHandlerCounts,
        IReadOnlySet<Type> eventsWithHandlers,
        IReadOnlySet<Type> requestsWithBehaviors,
        IReadOnlySet<Type> eventsWithBehaviors)
    {
        Probes = probes;
        RequestHandlerCounts = requestHandlerCounts;
        EventsWithHandlers = eventsWithHandlers;
        RequestsWithBehaviors = requestsWithBehaviors;
        EventsWithBehaviors = eventsWithBehaviors;
    }

    /// <summary>Describes the pipeline of one request or event type, keyed by that type.</summary>
    public IReadOnlyDictionary<Type, Func<IPipelineDescriber, PipelineDescription?>> Probes { get; }

    /// <summary>The number of handler descriptors registered for each request type.</summary>
    public IReadOnlyDictionary<Type, int> RequestHandlerCounts { get; }

    /// <summary>Event types with at least one handler descriptor.</summary>
    public IReadOnlySet<Type> EventsWithHandlers { get; }

    /// <summary>Request types a closed request behavior is registered for.</summary>
    public IReadOnlySet<Type> RequestsWithBehaviors { get; }

    /// <summary>Event types a closed event behavior is registered for.</summary>
    public IReadOnlySet<Type> EventsWithBehaviors { get; }

    public static SynapseRegistry Create(
        IServiceCollection services,
        IReadOnlyDictionary<Type, Func<IPipelineDescriber, PipelineDescription?>> probes)
    {
        var requestHandlerCounts = new Dictionary<Type, int>();
        var eventsWithHandlers = new HashSet<Type>();
        var requestsWithBehaviors = new HashSet<Type>();
        var eventsWithBehaviors = new HashSet<Type>();

        foreach (var descriptor in services)
        {
            var serviceType = descriptor.ServiceType;
            if (!serviceType.IsGenericType || serviceType.ContainsGenericParameters)
            {
                continue;
            }

            var definition = serviceType.GetGenericTypeDefinition();
            var target = serviceType.GetGenericArguments()[0];

            if (definition == typeof(IRequestHandler<>) || definition == typeof(IRequestHandler<,>))
            {
                requestHandlerCounts[target] = requestHandlerCounts.GetValueOrDefault(target) + 1;
            }
            else if (definition == typeof(IEventHandler<>))
            {
                eventsWithHandlers.Add(target);
            }
            else if (definition == typeof(IRequestPipelineBehavior<>) ||
                     definition == typeof(IRequestPipelineBehavior<,>))
            {
                requestsWithBehaviors.Add(target);
            }
            else if (definition == typeof(IEventPipelineBehavior<>))
            {
                eventsWithBehaviors.Add(target);
            }
        }

        return new SynapseRegistry(probes, requestHandlerCounts, eventsWithHandlers, requestsWithBehaviors,
            eventsWithBehaviors);
    }
}
```

- [ ] **Step 4: Record probes at every registration site**

In `SynapseConfig`: add field
`private readonly Dictionary<Type, Func<IPipelineDescriber, PipelineDescription?>> _probes = new();`
and record a probe right beside each `TryAdd` of a dispatcher, using closed generics:
- `RegisterRequestHandler<THandler, TRequest, TResponse>` (and its `When` variant, inside the `if (condition())` block): `_probes.TryAdd(typeof(TRequest), describer => describer.Describe<TRequest, TResponse>());`
- `RegisterRequestHandler<THandler, TRequest>` (and `When`): `_probes.TryAdd(typeof(TRequest), describer => describer.Describe<TRequest>());`
- `RegisterEventHandler<THandler, TEvent>` (and `When`): `_probes.TryAdd(typeof(TEvent), describer => describer.DescribeEvent<TEvent>());`

Stream handlers get no probe. In `AddRegisterGroup`'s `_actions.Add(svc => …)` block add:
```csharp
foreach (var (type, probe) in builder.Probes)
{
    _probes.TryAdd(type, probe);
}
```
In `Apply()`, after the `foreach (var action in _actions)` loop, add:
```csharp
services.AddSingleton(SynapseRegistry.Create(services, _probes));
```
Use `using UnambitiousFx.Synapse.Validation;`.

In `DefaultDependencyInjectionBuilder`: add the same `_probes` dictionary, expose
`public IReadOnlyDictionary<Type, Func<IPipelineDescriber, PipelineDescription?>> Probes => _probes;` and record the same probes at its six sites (the `When` variants record inside their `if (condition())`). Existing dispatcher lines are the anchors: put the probe line directly after each dispatcher `TryAdd`.

Lambda note: closing over the type parameters keeps it AOT safe; do not use `Describe(Type)`.

- [ ] **Step 5: Run to verify pass, then the full suite**

Run: `dotnet test --project test/Synapse.Tests -f net10.0 --filter-class "*SynapseRegistryTests"` → 7 passed.
Run: `dotnet build Synapse.slnx` → 0 warnings, 0 errors. Then `dotnet test --solution Synapse.slnx` → all green.

- [ ] **Step 6: Commit**

```bash
git add src/Synapse test/Synapse.Tests/Validation
git commit -m "feat(validation): capture handler/behavior registrations and pipeline probes"
```

---

### Task 3: Validator and `ValidateSynapse`

**Files:**
- Create: `src/Synapse/Validation/SynapseValidator.cs`
- Create: `src/Synapse/Validation/SynapseValidationExtensions.cs`
- Test: `test/Synapse.Tests/Validation/SynapseValidatorTests.cs`

**Interfaces:**
- Consumes: `SynapseRegistry` (Task 2), `SynapseValidationReport/Issue/Severity` (Task 1), `IPipelineDescriber`.
- Produces: `public static class SynapseValidationExtensions` (namespace `UnambitiousFx.Synapse`) with `public static SynapseValidationReport ValidateSynapse(this IServiceProvider services)`. Internal `SynapseValidator.Validate(SynapseRegistry, IPipelineDescriber)`.

Messages (use exactly these codes; wording may be improved but must name the type and the fix):
- SYN001: `A pipeline behavior is registered for '{Type}' but no handler is registered for it, so the behavior never runs. Register the handler or remove the behavior.`
- SYN002: `{n} handlers are registered for request '{Type}'. Only the first is used; the others are silently ignored. Keep one handler per request.`
- SYN003: `The pipeline for '{Type}' cannot be resolved: {ExceptionType}: {message}`
- SYN004: `Behaviors {A, B} of the pipeline for '{Type}' share Order {n}, so they run in registration order. Give them distinct Order values if the order matters.`

- [ ] **Step 1: Write the failing tests**

```csharp
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Tests.Validation;

[TestSubject(typeof(SynapseValidationExtensions))]
public sealed class SynapseValidatorTests
{
    [Fact]
    public void ValidateSynapse_WithACleanConfiguration_IsValidWithNoIssues()
    {
        // Arrange (Given)
        using var provider = Build(cfg =>
        {
            cfg.RegisterRequestHandler<PlainHandler, PlainCommand>();
            cfg.RegisterEventHandler<PingHandler, PingEvent>();
            cfg.RegisterRequestPipelineBehavior<OrderedBehavior<PlainCommand, First>, PlainCommand>();
        });

        // Act (When)
        var report = provider.ValidateSynapse();

        // Assert (Then)
        Assert.True(report.IsValid);
        Assert.Empty(report.Issues);
    }

    [Fact]
    public void ValidateSynapse_WithABehaviorForARequestWithoutHandler_ReportsSyn001()
    {
        // Arrange (Given)
        using var provider = Build(cfg =>
            cfg.RegisterRequestPipelineBehavior<OrderedBehavior<PlainCommand, First>, PlainCommand>());

        // Act (When)
        var report = provider.ValidateSynapse();

        // Assert (Then)
        var issue = Assert.Single(report.Errors);
        Assert.Equal("SYN001", issue.Code);
        Assert.Equal(typeof(PlainCommand), issue.Type);
        Assert.False(report.IsValid);
    }

    [Fact]
    public void ValidateSynapse_WithAnEventBehaviorForAnEventWithoutHandler_ReportsSyn001()
    {
        // Arrange (Given)
        using var provider = Build(cfg => cfg.RegisterEventPipelineBehavior<NoopEventBehavior, PingEvent>());

        // Act (When)
        var report = provider.ValidateSynapse();

        // Assert (Then)
        var issue = Assert.Single(report.Errors);
        Assert.Equal("SYN001", issue.Code);
        Assert.Equal(typeof(PingEvent), issue.Type);
    }

    [Fact]
    public void ValidateSynapse_WithTwoHandlersForOneRequest_ReportsSyn002()
    {
        // Arrange (Given)
        using var provider = Build(cfg =>
        {
            cfg.RegisterRequestHandler<PlainHandler, PlainCommand>();
            cfg.RegisterRequestHandler<OtherPlainHandler, PlainCommand>();
        });

        // Act (When)
        var report = provider.ValidateSynapse();

        // Assert (Then)
        var issue = Assert.Single(report.Errors);
        Assert.Equal("SYN002", issue.Code);
        Assert.Equal(typeof(PlainCommand), issue.Type);
    }

    [Fact]
    public void ValidateSynapse_WithSeveralHandlersForOneEvent_IsValid()
    {
        // Arrange (Given) — fan-out to many subscribers is the point of events
        using var provider = Build(cfg =>
        {
            cfg.RegisterEventHandler<PingHandler, PingEvent>();
            cfg.RegisterEventHandler<OtherPingHandler, PingEvent>();
        });

        // Act (When)
        var report = provider.ValidateSynapse();

        // Assert (Then)
        Assert.True(report.IsValid);
        Assert.Empty(report.Issues);
    }

    [Fact]
    public void ValidateSynapse_WhenAHandlerDependencyIsMissing_ReportsSyn003WithTheCause()
    {
        // Arrange (Given)
        using var provider = Build(cfg => cfg.RegisterRequestHandler<NeedyHandler, NeedyCommand>());

        // Act (When)
        var report = provider.ValidateSynapse();

        // Assert (Then)
        var issue = Assert.Single(report.Errors);
        Assert.Equal("SYN003", issue.Code);
        Assert.Equal(typeof(NeedyCommand), issue.Type);
        Assert.Contains(nameof(IMissingDependency), issue.Message);
    }

    [Fact]
    public void ValidateSynapse_WithTwoBehaviorsSharingAnOrder_ReportsSyn004AsAWarning()
    {
        // Arrange (Given)
        using var provider = Build(cfg =>
        {
            cfg.RegisterRequestHandler<PlainHandler, PlainCommand>();
            cfg.RegisterRequestPipelineBehavior<OrderedBehavior<PlainCommand, First>, PlainCommand>();
            cfg.RegisterRequestPipelineBehavior<OrderedBehavior<PlainCommand, Second>, PlainCommand>();
        });

        // Act (When)
        var report = provider.ValidateSynapse();

        // Assert (Then)
        var issue = Assert.Single(report.Warnings);
        Assert.Equal("SYN004", issue.Code);
        Assert.Equal(typeof(PlainCommand), issue.Type);
        Assert.True(report.IsValid);
    }

    [Fact]
    public void ValidateSynapse_WithBehaviorsOfDistinctOrders_ReportsNoTie()
    {
        // Arrange (Given)
        using var provider = Build(cfg =>
        {
            cfg.RegisterRequestHandler<PlainHandler, PlainCommand>();
            cfg.RegisterRequestPipelineBehavior<OrderedBehavior<PlainCommand, First>, PlainCommand>();
            cfg.RegisterRequestPipelineBehavior<OrderedBehavior<PlainCommand, Third>, PlainCommand>();
        });

        // Act (When)
        var report = provider.ValidateSynapse();

        // Assert (Then)
        Assert.Empty(report.Issues);
    }

    [Fact]
    public void ValidateSynapse_WithAnOrderTieOnAnEventPipeline_ReportsSyn004()
    {
        // Arrange (Given)
        using var provider = Build(cfg =>
        {
            cfg.RegisterEventHandler<PingHandler, PingEvent>();
            cfg.RegisterEventPipelineBehavior<TiedEventBehaviorA, PingEvent>();
            cfg.RegisterEventPipelineBehavior<TiedEventBehaviorB, PingEvent>();
        });

        // Act (When)
        var report = provider.ValidateSynapse();

        // Assert (Then)
        Assert.Equal("SYN004", Assert.Single(report.Warnings).Code);
    }

    [Fact]
    public void ValidateSynapse_WithSeveralProblems_ReportsAllOfThem()
    {
        // Arrange (Given)
        using var provider = Build(cfg =>
        {
            cfg.RegisterRequestHandler<PlainHandler, PlainCommand>();
            cfg.RegisterRequestHandler<OtherPlainHandler, PlainCommand>();
            cfg.RegisterEventPipelineBehavior<NoopEventBehavior, PingEvent>();
        });

        // Act (When)
        var report = provider.ValidateSynapse();

        // Assert (Then)
        Assert.Equal(["SYN001", "SYN002"], report.Errors.Select(issue => issue.Code).Order());
    }

    [Fact]
    public void ValidateSynapse_WhenAddSynapseWasNeverCalled_ThrowsInvalidOperation()
    {
        // Arrange (Given)
        using var provider = new ServiceCollection().BuildServiceProvider();

        // Act (When) / Assert (Then)
        Assert.Throws<InvalidOperationException>(() => provider.ValidateSynapse());
    }

    [Fact]
    public void ValidateSynapse_WithNullProvider_Throws()
    {
        // Arrange (Given) / Act (When) / Assert (Then)
        Assert.Throws<ArgumentNullException>(() => SynapseValidationExtensions.ValidateSynapse(null!));
    }

    private static ServiceProvider Build(Action<ISynapseConfig> configure)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSynapse(configure);
        return services.BuildServiceProvider();
    }

    private sealed record PlainCommand : IRequest;

    private sealed record NeedyCommand : IRequest;

    private sealed record PingEvent : IEvent;

    private interface IMissingDependency;

    private sealed class First;

    private sealed class Second;

    private sealed class Third;

    private sealed class PlainHandler : IRequestHandler<PlainCommand>
    {
        public ValueTask<Result> HandleAsync(PlainCommand request, CancellationToken cancellationToken = default) =>
            new(Result.Success());
    }

    private sealed class OtherPlainHandler : IRequestHandler<PlainCommand>
    {
        public ValueTask<Result> HandleAsync(PlainCommand request, CancellationToken cancellationToken = default) =>
            new(Result.Success());
    }

    private sealed class NeedyHandler(IMissingDependency dependency) : IRequestHandler<NeedyCommand>
    {
        public ValueTask<Result> HandleAsync(NeedyCommand request, CancellationToken cancellationToken = default) =>
            new(Result.Success());
    }

    private sealed class PingHandler : IEventHandler<PingEvent>
    {
        public ValueTask<Result> HandleAsync(PingEvent @event, CancellationToken cancellationToken = default) =>
            new(Result.Success());
    }

    private sealed class OtherPingHandler : IEventHandler<PingEvent>
    {
        public ValueTask<Result> HandleAsync(PingEvent @event, CancellationToken cancellationToken = default) =>
            new(Result.Success());
    }

    // The marker type picks the Order so two closed behaviors can tie or differ: First/Second => 10, Third => 20.
    private sealed class OrderedBehavior<TRequest, TMarker> : IRequestPipelineBehavior<TRequest>,
        IOrderedPipelineBehavior
        where TRequest : IRequest
    {
        public uint Order => typeof(TMarker) == typeof(Third) ? 20u : 10u;

        public ValueTask<Result> HandleAsync(TRequest request, RequestHandlerDelegate<TRequest> next,
            CancellationToken cancellationToken = default) => next(request, cancellationToken);
    }

    private sealed class NoopEventBehavior : IEventPipelineBehavior<PingEvent>
    {
        public ValueTask<Result> HandleAsync(PingEvent @event, EventHandlerDelegate<PingEvent> next,
            CancellationToken cancellationToken = default) => next(@event, cancellationToken);
    }

    private sealed class TiedEventBehaviorA : IEventPipelineBehavior<PingEvent>, IOrderedPipelineBehavior
    {
        public uint Order => 5;

        public ValueTask<Result> HandleAsync(PingEvent @event, EventHandlerDelegate<PingEvent> next,
            CancellationToken cancellationToken = default) => next(@event, cancellationToken);
    }

    private sealed class TiedEventBehaviorB : IEventPipelineBehavior<PingEvent>, IOrderedPipelineBehavior
    {
        public uint Order => 5;

        public ValueTask<Result> HandleAsync(PingEvent @event, EventHandlerDelegate<PingEvent> next,
            CancellationToken cancellationToken = default) => next(@event, cancellationToken);
    }
}
```

Same compile-fix note as Task 2: mirror `PipelineDescriberTests.cs` for any helper shape that does not compile. `typeof(TMarker)` comparison is only a test double, not production code.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test --project test/Synapse.Tests -f net10.0 --filter-class "*SynapseValidatorTests"`
Expected: build FAIL (`ValidateSynapse` missing).

- [ ] **Step 3: Implement**

`src/Synapse/Validation/SynapseValidator.cs`:
```csharp
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Validation;

internal static class SynapseValidator
{
    public static SynapseValidationReport Validate(SynapseRegistry registry, IPipelineDescriber describer)
    {
        var issues = new List<SynapseValidationIssue>();

        foreach (var request in registry.RequestsWithBehaviors.Where(type =>
                     !registry.RequestHandlerCounts.ContainsKey(type)))
        {
            issues.Add(NoHandler(request));
        }

        foreach (var @event in registry.EventsWithBehaviors.Where(type => !registry.EventsWithHandlers.Contains(type)))
        {
            issues.Add(NoHandler(@event));
        }

        foreach (var (request, count) in registry.RequestHandlerCounts.Where(pair => pair.Value > 1))
        {
            issues.Add(new SynapseValidationIssue("SYN002", SynapseValidationSeverity.Error,
                $"{count} handlers are registered for request '{request}'. Only the first is used; the others are " +
                "silently ignored. Keep one handler per request.", request));
        }

        foreach (var (type, probe) in registry.Probes)
        {
            PipelineDescription? description;
            try
            {
                description = probe(describer);
            }
            catch (Exception exception)
            {
                issues.Add(new SynapseValidationIssue("SYN003", SynapseValidationSeverity.Error,
                    $"The pipeline for '{type}' cannot be resolved: {exception.GetType().Name}: {exception.Message}",
                    type));
                continue;
            }

            if (description is null)
            {
                continue;
            }

            foreach (var tie in description.Behaviors.GroupBy(behavior => behavior.Order).Where(g => g.Count() > 1))
            {
                var names = string.Join(", ", tie.Select(behavior => behavior.Type.Name));
                issues.Add(new SynapseValidationIssue("SYN004", SynapseValidationSeverity.Warning,
                    $"Behaviors {names} of the pipeline for '{type}' share Order {tie.Key}, so they run in " +
                    "registration order. Give them distinct Order values if the order matters.", type));
            }
        }

        return new SynapseValidationReport(issues
            .OrderBy(issue => issue.Code, StringComparer.Ordinal)
            .ThenBy(issue => issue.Type.FullName, StringComparer.Ordinal));
    }

    private static SynapseValidationIssue NoHandler(Type type)
    {
        return new SynapseValidationIssue("SYN001", SynapseValidationSeverity.Error,
            $"A pipeline behavior is registered for '{type}' but no handler is registered for it, so the behavior " +
            "never runs. Register the handler or remove the behavior.", type);
    }
}
```

`src/Synapse/Validation/SynapseValidationExtensions.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Validation;

namespace UnambitiousFx.Synapse;

/// <summary>
///     Checks a Synapse configuration for mistakes that otherwise only show up at the first request.
/// </summary>
public static class SynapseValidationExtensions
{
    /// <summary>
    ///     Validates what <c>AddSynapse</c> registered: a behavior registered for a request or event without a
    ///     handler (SYN001), several handlers for one request (SYN002), a pipeline that cannot be resolved
    ///     (SYN003), and behaviors sharing an <c>Order</c> (SYN004, a warning).
    /// </summary>
    /// <param name="services">The built service provider.</param>
    /// <returns>The report. It is never thrown; call <see cref="SynapseValidationReport.ThrowIfInvalid" /> to fail.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services" /> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException"><c>AddSynapse</c> was never called on this provider's collection.</exception>
    /// <remarks>
    ///     Resolves every registered handler and its behaviors once, like <see cref="IPipelineDescriber" />, so a
    ///     handler whose constructor needs something that only exists inside a real request reports SYN003.
    ///     Behaviors added to the service collection after <c>AddSynapse</c> returned are not seen, and open-generic
    ///     behaviors are not checked.
    /// </remarks>
    public static SynapseValidationReport ValidateSynapse(this IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var registry = services.GetService<SynapseRegistry>() ??
                       throw new InvalidOperationException(
                           "Synapse is not registered. Call AddSynapse on the service collection first.");
        return SynapseValidator.Validate(registry, services.GetRequiredService<IPipelineDescriber>());
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test --project test/Synapse.Tests -f net10.0 --filter-class "*SynapseValidatorTests"` → 12 passed.
Then `dotnet build Synapse.slnx` (no AOT/trim warnings) and `dotnet test --solution Synapse.slnx`.
Mutation check: temporarily change SYN002's `> 1` to `> 2` and confirm `…ReportsSyn002` fails; revert.

- [ ] **Step 5: Commit**

```bash
git add src/Synapse/Validation test/Synapse.Tests/Validation
git commit -m "feat(validation): add ValidateSynapse with SYN001-SYN004 checks"
```

---

### Task 4: `ValidateOnStart`

**Files:**
- Create: `src/Synapse/Validation/SynapseValidationStartup.cs`
- Modify: `src/Synapse/ISynapseConfig.cs` (add `ValidateOnStart()`), `src/Synapse/SynapseConfig.cs` (flag + `Apply`)
- Test: `test/Synapse.Tests/Validation/SynapseValidationStartupTests.cs`

**Interfaces:**
- Consumes: `ValidateSynapse` (Task 3).
- Produces: `ISynapseConfig ValidateOnStart()`; internal `SynapseValidationStartup : IHostedService`, registered only when `ValidateOnStart()` was called.

- [ ] **Step 1: Write the failing tests**

```csharp
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Tests.Validation;

[TestSubject(typeof(ISynapseConfig))]
public sealed class SynapseValidationStartupTests
{
    [Fact]
    public async Task StartAsync_WithAnError_ThrowsSynapseValidationException()
    {
        // Arrange (Given)
        await using var provider = Build(new LogSink(), cfg =>
        {
            cfg.RegisterEventPipelineBehavior<NoopEventBehavior, PingEvent>();
            cfg.ValidateOnStart();
        });

        // Act (When)
        var exception = await Record.ExceptionAsync(() => StartHostedServices(provider));

        // Assert (Then)
        var thrown = Assert.IsType<SynapseValidationException>(exception);
        Assert.Equal("SYN001", Assert.Single(thrown.Report.Errors).Code);
    }

    [Fact]
    public async Task StartAsync_WithOnlyWarnings_StartsAndLogsThem()
    {
        // Arrange (Given)
        var sink = new LogSink();
        await using var provider = Build(sink, cfg =>
        {
            cfg.RegisterEventHandler<PingHandler, PingEvent>();
            cfg.RegisterEventPipelineBehavior<TiedEventBehaviorA, PingEvent>();
            cfg.RegisterEventPipelineBehavior<TiedEventBehaviorB, PingEvent>();
            cfg.ValidateOnStart();
        });

        // Act (When)
        await StartHostedServices(provider);

        // Assert (Then)
        Assert.Contains(sink.Warnings, message => message.Contains("SYN004"));
    }

    [Fact]
    public async Task StartAsync_WithACleanConfiguration_StartsWithoutLoggingWarnings()
    {
        // Arrange (Given)
        var sink = new LogSink();
        await using var provider = Build(sink, cfg =>
        {
            cfg.RegisterEventHandler<PingHandler, PingEvent>();
            cfg.ValidateOnStart();
        });

        // Act (When)
        await StartHostedServices(provider);

        // Assert (Then)
        Assert.Empty(sink.Warnings);
    }

    [Fact]
    public async Task StartAsync_WithoutValidateOnStart_DoesNotValidate()
    {
        // Arrange (Given) — same misconfiguration as the first test, but never opted in
        await using var provider = Build(new LogSink(),
            cfg => cfg.RegisterEventPipelineBehavior<NoopEventBehavior, PingEvent>());

        // Act (When)
        var exception = await Record.ExceptionAsync(() => StartHostedServices(provider));

        // Assert (Then)
        Assert.Null(exception);
    }

    [Fact]
    public void ValidateOnStart_CalledTwice_RegistersOneHostedService()
    {
        // Arrange (Given)
        var services = new ServiceCollection().AddLogging();
        services.AddSynapse(cfg =>
        {
            cfg.ValidateOnStart();
            cfg.ValidateOnStart();
        });

        // Act (When)
        var count = services.Count(descriptor => descriptor.ServiceType == typeof(IHostedService) &&
                                                 descriptor.ImplementationType?.Name == "SynapseValidationStartup");

        // Assert (Then)
        Assert.Equal(1, count);
    }

    private static async Task StartHostedServices(IServiceProvider provider)
    {
        foreach (var hosted in provider.GetServices<IHostedService>())
        {
            await hosted.StartAsync(TestContext.Current.CancellationToken);
        }
    }

    private static ServiceProvider Build(LogSink sink, Action<ISynapseConfig> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(sink));
        services.AddSynapse(configure);
        return services.BuildServiceProvider();
    }

    private sealed record PingEvent : IEvent;

    private sealed class PingHandler : IEventHandler<PingEvent>
    {
        public ValueTask<Result> HandleAsync(PingEvent @event, CancellationToken cancellationToken = default) =>
            new(Result.Success());
    }

    private sealed class NoopEventBehavior : IEventPipelineBehavior<PingEvent>
    {
        public ValueTask<Result> HandleAsync(PingEvent @event, EventHandlerDelegate<PingEvent> next,
            CancellationToken cancellationToken = default) => next(@event, cancellationToken);
    }

    private sealed class TiedEventBehaviorA : IEventPipelineBehavior<PingEvent>, IOrderedPipelineBehavior
    {
        public uint Order => 5;

        public ValueTask<Result> HandleAsync(PingEvent @event, EventHandlerDelegate<PingEvent> next,
            CancellationToken cancellationToken = default) => next(@event, cancellationToken);
    }

    private sealed class TiedEventBehaviorB : IEventPipelineBehavior<PingEvent>, IOrderedPipelineBehavior
    {
        public uint Order => 5;

        public ValueTask<Result> HandleAsync(PingEvent @event, EventHandlerDelegate<PingEvent> next,
            CancellationToken cancellationToken = default) => next(@event, cancellationToken);
    }

    private sealed class LogSink : ILoggerProvider, ILogger
    {
        public List<string> Warnings { get; } = [];

        public ILogger CreateLogger(string categoryName) => this;

        public void Dispose()
        {
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }
}
```
Caution: `LogSink` also captures warnings from other Synapse components (for example the in-memory outbox production check is only logged in Production, so it stays silent under the default test host). If `StartAsync_WithACleanConfiguration…` sees an unrelated warning, filter `sink.Warnings` to messages containing `SYN` instead of weakening the assertion.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test --project test/Synapse.Tests -f net10.0 --filter-class "*SynapseValidationStartupTests"`
Expected: build FAIL (`ValidateOnStart` missing).

- [ ] **Step 3: Implement**

`src/Synapse/ISynapseConfig.cs`: add near the other infrastructure members:
```csharp
    /// <summary>
    ///     Validates the Synapse configuration when the host starts and fails the start when it is invalid, instead
    ///     of failing at the first request. Errors throw <see cref="SynapseValidationException" />; warnings are
    ///     logged. Only generic-host applications start hosted services; elsewhere call
    ///     <c>ValidateSynapse()</c> yourself. See <see cref="SynapseValidationExtensions.ValidateSynapse" />.
    /// </summary>
    /// <returns>The same config, for chaining.</returns>
    ISynapseConfig ValidateOnStart();
```

`src/Synapse/SynapseConfig.cs`: add `private bool _validateOnStart;`, then
```csharp
    public ISynapseConfig ValidateOnStart()
    {
        _validateOnStart = true;
        return this;
    }
```
and in `Apply()` next to the `InMemoryOutboxProductionCheck` line:
```csharp
        if (_validateOnStart)
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, SynapseValidationStartup>());
        }
```
`src/Synapse/Validation/SynapseValidationStartup.cs`:
```csharp
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace UnambitiousFx.Synapse.Validation;

/// <summary>
///     Runs <c>ValidateSynapse</c> when the host starts; registered by <c>ValidateOnStart()</c>.
/// </summary>
internal sealed class SynapseValidationStartup(IServiceProvider services, ILogger<SynapseValidationStartup> logger)
    : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var report = services.ValidateSynapse();
        foreach (var warning in report.Warnings)
        {
            logger.LogWarning("{Code}: {Message}", warning.Code, warning.Message);
        }

        report.ThrowIfInvalid();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
```
If any other `ISynapseConfig` implementation exists in the repo (test doubles, other projects), `dotnet build Synapse.slnx` will name it; implement `ValidateOnStart` there too.

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test --project test/Synapse.Tests -f net10.0 --filter-class "*SynapseValidationStartupTests"` → 5 passed. Then `dotnet build Synapse.slnx`, `dotnet test --solution Synapse.slnx`.

- [ ] **Step 5: Commit**

```bash
git add src/Synapse test/Synapse.Tests/Validation
git commit -m "feat(validation): add ValidateOnStart hosted check"
```

---

### Task 5: Example, docs and Native AOT proof

**Files:**
- Modify: `examples/MinimalApi/Program.cs` (add `cfg.ValidateOnStart();` inside the `AddSynapse` callback)
- Modify: `docs/docs/pipelines.mdx` (new section "Validating the configuration" before `## See also`)
- Test: `examples/MinimalApi.Tests/` (one test that the app starts with validation on; use the existing `PipelinesApiTests` factory pattern)

**Interfaces:**
- Consumes: `ValidateOnStart()`, `ValidateSynapse()`.

- [ ] **Step 1: Add the example call and prove it starts cleanly**

Add `cfg.ValidateOnStart();` at the end of the `AddSynapse` lambda in `examples/MinimalApi/Program.cs`, with a one-line comment that a misconfiguration now fails startup. Write a test in `examples/MinimalApi.Tests` mirroring `PipelinesApiTests` that builds the app through the same `WebApplicationFactory`, resolves `IServiceProvider.ValidateSynapse()` from `factory.Services` and asserts `report.IsValid`. Print `report.ToString()` as the assertion message so a failure names the finding.

- [ ] **Step 2: Run it**

Run: `dotnet test --project examples/MinimalApi.Tests -f net10.0` → pass. If it reports errors, they are real findings in the example or false positives in the validator: read each one and decide. A false positive means a bug in Task 2/3 code; fix it there with a regression test in `SynapseValidatorTests`, do not silence it in the example. For example, a built-in behavior registered for every event would produce SYN001.

- [ ] **Step 3: Native AOT check**

Run: `dotnet publish examples/MinimalApi/MinimalApi.csproj -c Release -r osx-arm64 --self-contained` (use the local RID) → publishes without IL/AOT warnings; run the binary and confirm it starts (`curl -fsS localhost:5000/` or the port it prints), then stop it. Startup would have thrown if validation misbehaved under AOT.

- [ ] **Step 4: Docs**

In `docs/docs/pipelines.mdx` add `## Validating the configuration` before `## See also`: what it catches (table of SYN001–SYN004 with severity and fix), a test snippet
```csharp
var report = provider.ValidateSynapse();
Assert.True(report.IsValid, report.ToString());
```
a startup snippet `cfg.ValidateOnStart();`, and a "What it cannot catch" list: a request with neither handler nor behavior, `[assembly: SynapseGlobalBehavior]` when the host's generated group is never registered (`cfg.AddRegisterGroup(new Host.RegisterGroup())`; a build-time analyzer is tracked in #103), stream requests, behaviors added after `AddSynapse`, open-generic behaviors. Link the new section from the "Applying a behavior to every handler" section where it mentions the silent no-op, if such a sentence exists. Then `cd docs && pnpm build` and confirm zero broken-link warnings.

- [ ] **Step 5: Full verification and commit**

Run: `dotnet build Synapse.slnx` (0 warnings) and `dotnet test --solution Synapse.slnx` (all green).

```bash
git add examples docs/docs/pipelines.mdx
git commit -m "feat(validation): validate MinimalApi on start and document ValidateSynapse"
```
