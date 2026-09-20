# IPipelineDescriber Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let users ask Synapse which handler(s) and behaviors, in execution order, a request or event type resolves to, so an architecture test can assert that security behaviors are never skipped.

**Architecture:** The description is read from the component that actually runs the pipeline. `ProxyRequestHandler` already holds its sorted behavior array and exposes it through an internal `IPipelineInfo`; events share one internal "resolve handlers + sorted behaviors" method between `EventDispatcher` and the describer. A singleton `PipelineDescriber` opens a DI scope per call, resolves, builds an immutable description of types and orders, and disposes the scope. Nothing changes on the dispatch path.

**Tech Stack:** C# / .NET 8-10 (multi-target), Microsoft.Extensions.DependencyInjection, xUnit v3 on Microsoft Testing Platform, NSubstitute, Docusaurus (`docs/`).

**Spec:** `docs/superpowers/specs/2026-09-20-pipeline-describer-design.md` (issue #101, sub-issue of #96).

## Global Constraints

- File-scoped namespaces (`namespace UnambitiousFx.Synapse;`). Always braces, even for single statements.
- Naming: `PascalCase` public/types/methods, `camelCase` params/locals, `_camelCase` private fields, `IPascalCase` interfaces.
- XML doc comments (`<summary>`/`<param>`/`<returns>`) on every public API. Comments explain "why", used sparingly.
- Don't expose internals: `IPipelineInfo`, `PipelineDescriber`, `EventPipelineParts` are `internal`.
- Every `src/` library has `IsAotCompatible=true` and warnings fail the build: the generic API must produce zero AOT/trim warnings.
- Tests: AAA with `// Arrange (Given)`, `// Act (When)`, `// Assert (Then)` comments; names `Method_Scenario_ExpectedBehavior`; `TestContext.Current.CancellationToken` for tokens (xUnit v3); `Assert.DoesNotContain`/`Assert.Single` rather than `Assert.Empty`-style patterns the xUnit analyzers reject (analyzer errors fail the build).
- Test command (Microsoft Testing Platform, NOT `--filter`): `dotnet test --project test/Synapse.Tests -f net10.0 --filter-class "*ClassName"` or `--filter-method "*Name*"`. Full suite: `dotnet test --solution Synapse.slnx`.
- zsh: quote globs (`--include='*.cs'`); BSD `sed` has no `\n` in replacements, use the Edit tool.
- Commit messages end with the trailer `Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>`.
- Work on branch `feat/pipeline-describer` (already created from `main`, spec already committed). Do not touch the untracked `docs/endpoints/` directory; stage files explicitly, never `git add -A`.

## File Structure

| File | Responsibility |
|---|---|
| `src/Synapse.Abstractions/PipelineDescription.cs` (create) | Public record: handlers + behaviors of one message type |
| `src/Synapse.Abstractions/BehaviorDescription.cs` (create) | Public record: one behavior's type and `Order` |
| `src/Synapse.Abstractions/IPipelineDescriber.cs` (create) | Public API (grows across tasks 1-3) |
| `src/Synapse/Pipelines/IPipelineInfo.cs` (create) | Internal contract a proxy implements to describe itself |
| `src/Synapse/Pipelines/PipelineDescriber.cs` (create) | Internal singleton implementing `IPipelineDescriber` |
| `src/Synapse/Pipelines/PipelineBehaviorOrdering.cs` (modify) | Add `Describe(...)` helper next to `OrderOf` |
| `src/Synapse/ProxyRequestHandler.cs` (modify) | Both proxies implement `IPipelineInfo` |
| `src/Synapse/Publish/EventPipelineParts.cs` (create) | Shared resolve of event handlers + sorted behaviors |
| `src/Synapse/Publish/EventDispatcher.cs` (modify) | `BuildPipeline` uses `EventPipelineParts` |
| `src/Synapse/DependencyInjectionExtensions.cs` (modify) | Register `IPipelineDescriber` singleton |
| `test/Synapse.Tests/Pipelines/PipelineDescriberTests.cs` (create) | All describer tests |
| `examples/MinimalApi/Program.cs` (modify) | `GET /pipelines/create-task` endpoint |
| `examples/MinimalApi.Tests/PipelinesApiTests.cs` (create) | Endpoint test |
| `.github/workflows/ci.yml` (modify) | AOT job curls the new endpoint |
| `docs/docs/pipelines.mdx` (modify) | "Inspecting a pipeline" section |

---

### Task 1: Describe request pipelines

**Files:**
- Create: `src/Synapse.Abstractions/PipelineDescription.cs`, `src/Synapse.Abstractions/BehaviorDescription.cs`, `src/Synapse.Abstractions/IPipelineDescriber.cs`, `src/Synapse/Pipelines/IPipelineInfo.cs`, `src/Synapse/Pipelines/PipelineDescriber.cs`
- Modify: `src/Synapse/Pipelines/PipelineBehaviorOrdering.cs`, `src/Synapse/ProxyRequestHandler.cs`, `src/Synapse/DependencyInjectionExtensions.cs`
- Test: `test/Synapse.Tests/Pipelines/PipelineDescriberTests.cs`

**Interfaces:**
- Consumes: `PipelineBehaviorOrdering.OrderOf(object) : uint` (existing, `src/Synapse/Pipelines/PipelineBehaviorOrdering.cs`); `IRequestHandler<TRequest>` / `IRequestHandler<TRequest,TResponse>` resolved through the proxies.
- Produces (later tasks rely on these exact names):
  - `public sealed record BehaviorDescription(Type Type, uint Order)`
  - `public sealed record PipelineDescription(IReadOnlyList<Type> Handlers, IReadOnlyList<BehaviorDescription> Behaviors)`
  - `public interface IPipelineDescriber` with `PipelineDescription? Describe<TRequest>() where TRequest : IRequest` and `PipelineDescription? Describe<TRequest, TResponse>() where TRequest : IRequest<TResponse> where TResponse : notnull`
  - `internal static IReadOnlyList<BehaviorDescription> PipelineBehaviorOrdering.Describe(IReadOnlyList<object> sortedBehaviors)`
  - `internal sealed class PipelineDescriber : IPipelineDescriber` (constructor takes `IServiceScopeFactory`), registered singleton.

- [ ] **Step 1: Add the public types**

`src/Synapse.Abstractions/BehaviorDescription.cs`:

```csharp
namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     One pipeline behavior in a <see cref="PipelineDescription" />.
/// </summary>
/// <param name="Type">The concrete behavior type that runs, e.g. a closed generic such as <c>AuditBehavior&lt;CreateTask&gt;</c>.</param>
/// <param name="Order">
///     The position the behavior declared through <see cref="IOrderedPipelineBehavior" />, or
///     <see cref="IOrderedPipelineBehavior.Last" /> when it declares none.
/// </param>
public sealed record BehaviorDescription(Type Type, uint Order);
```

`src/Synapse.Abstractions/PipelineDescription.cs`:

```csharp
namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     The handler(s) and behaviors a message type resolves to, as the dispatcher runs them.
/// </summary>
/// <param name="Handlers">
///     The handler types: exactly one for a request, one per subscriber for an event.
/// </param>
/// <param name="Behaviors">
///     The behaviors in execution order, outermost first. Behaviors that share an <c>Order</c> keep their
///     registration order.
/// </param>
public sealed record PipelineDescription(
    IReadOnlyList<Type> Handlers,
    IReadOnlyList<BehaviorDescription> Behaviors);
```

`src/Synapse.Abstractions/IPipelineDescriber.cs`:

```csharp
namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Reports the pipeline a request or event type resolves to, so a test can assert on it, for example that every
///     request traverses the security behaviors.
/// </summary>
/// <remarks>
///     Describing resolves the behaviors from a fresh DI scope, because <see cref="IOrderedPipelineBehavior.Order" />
///     is an instance property. A behavior whose constructor needs something that only exists inside a real request
///     can therefore throw here, and a handler that only implements <see cref="IAsyncDisposable" /> makes disposing
///     the scope throw.
/// </remarks>
public interface IPipelineDescriber
{
    /// <summary>
    ///     Describes the pipeline of a request that produces no response.
    /// </summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <returns>The description, or <c>null</c> when no handler is registered for the request.</returns>
    PipelineDescription? Describe<TRequest>()
        where TRequest : IRequest;

    /// <summary>
    ///     Describes the pipeline of a request that produces a response.
    /// </summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <returns>The description, or <c>null</c> when no handler is registered for the request.</returns>
    PipelineDescription? Describe<TRequest, TResponse>()
        where TRequest : IRequest<TResponse>
        where TResponse : notnull;
}
```

- [ ] **Step 2: Add the internal skeleton so tests compile (returns `null` for now)**

`src/Synapse/Pipelines/IPipelineInfo.cs`:

```csharp
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Pipelines;

/// <summary>
///     Implemented by the request proxies so a description is read from the chain the dispatcher actually runs,
///     instead of being computed a second time.
/// </summary>
internal interface IPipelineInfo
{
    Type HandlerType { get; }

    IReadOnlyList<BehaviorDescription> Behaviors { get; }
}
```

`src/Synapse/Pipelines/PipelineDescriber.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Pipelines;

/// <summary>
///     Default <see cref="IPipelineDescriber" />: opens a scope per call, reads the pipeline from what the
///     container resolves, and disposes the scope.
/// </summary>
internal sealed class PipelineDescriber : IPipelineDescriber
{
    private readonly IServiceScopeFactory _scopeFactory;

    public PipelineDescriber(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public PipelineDescription? Describe<TRequest>()
        where TRequest : IRequest
    {
        return null;
    }

    public PipelineDescription? Describe<TRequest, TResponse>()
        where TRequest : IRequest<TResponse>
        where TResponse : notnull
    {
        return null;
    }
}
```

In `src/Synapse/DependencyInjectionExtensions.cs`, inside `AddSynapse`, directly after the line `services.TryAddScoped<IOutboxDiscard, OutboxDiscard>();`, add:

```csharp
        services.TryAddSingleton<IPipelineDescriber, PipelineDescriber>();
```

(`Synapse.Pipelines` is already imported there for the CQRS behavior registrations; if the build says otherwise, add `using UnambitiousFx.Synapse.Pipelines;`.)

- [ ] **Step 3: Write the failing tests**

`test/Synapse.Tests/Pipelines/PipelineDescriberTests.cs`:

```csharp
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Pipelines;

namespace UnambitiousFx.Synapse.Tests.Pipelines;

[TestSubject(typeof(PipelineDescriber))]
public sealed class PipelineDescriberTests
{
    [Fact]
    public async Task Describe_WithOrderedBehaviors_ListsThemOutermostFirstAsTheyExecute()
    {
        // Arrange (Given) — registered out of order on purpose
        var trace = new Trace();
        await using var provider = Build(trace, cfg =>
        {
            cfg.RegisterRequestPipelineBehavior<InnerBehavior<PlainCommand>, PlainCommand>();
            cfg.RegisterRequestPipelineBehavior<UnorderedBehavior<PlainCommand>, PlainCommand>();
            cfg.RegisterRequestPipelineBehavior<OuterBehavior<PlainCommand>, PlainCommand>();
        });
        var describer = provider.GetRequiredService<IPipelineDescriber>();

        // Act (When)
        var description = describer.Describe<PlainCommand>();
        await Invoke(provider, new PlainCommand());

        // Assert (Then) — the description is the executed chain, not a second opinion about it
        Assert.NotNull(description);
        Assert.Equal(new[] { typeof(PlainCommandHandler) }, description.Handlers);
        Assert.Equal(
            new[]
            {
                typeof(OuterBehavior<PlainCommand>),
                typeof(InnerBehavior<PlainCommand>),
                typeof(UnorderedBehavior<PlainCommand>)
            },
            description.Behaviors.Select(behavior => behavior.Type));
        Assert.Equal(new uint[] { 5, 20, IOrderedPipelineBehavior.Last },
            description.Behaviors.Select(behavior => behavior.Order));
        Assert.Equal(new[] { "Outer", "Inner", "Unordered", "handler" }, trace.Steps);
    }

    [Fact]
    public void Describe_WithBehaviorsSharingAnOrder_KeepsRegistrationOrder()
    {
        // Arrange (Given)
        using var provider = Build(new Trace(), cfg =>
        {
            cfg.RegisterRequestPipelineBehavior<FirstTieBehavior<PlainCommand>, PlainCommand>();
            cfg.RegisterRequestPipelineBehavior<SecondTieBehavior<PlainCommand>, PlainCommand>();
        });

        // Act (When)
        var description = provider.GetRequiredService<IPipelineDescriber>().Describe<PlainCommand>();

        // Assert (Then)
        Assert.NotNull(description);
        Assert.Equal(
            new[] { typeof(FirstTieBehavior<PlainCommand>), typeof(SecondTieBehavior<PlainCommand>) },
            description.Behaviors.Select(behavior => behavior.Type));
    }

    [Fact]
    public void Describe_WithARequestThatHasAResponse_DescribesItsHandlerAndBehaviors()
    {
        // Arrange (Given)
        using var provider = Build(new Trace(),
            cfg => cfg.RegisterRequestPipelineBehavior<CountBehavior, CountQuery, int>());

        // Act (When)
        var description = provider.GetRequiredService<IPipelineDescriber>().Describe<CountQuery, int>();

        // Assert (Then)
        Assert.NotNull(description);
        Assert.Equal(new[] { typeof(CountQueryHandler) }, description.Handlers);
        Assert.Equal(new[] { typeof(CountBehavior) }, description.Behaviors.Select(behavior => behavior.Type));
    }

    [Fact]
    public void Describe_WithNoBehaviors_ReturnsAnEmptyBehaviorList()
    {
        // Arrange (Given)
        using var provider = Build(new Trace(), _ => { });

        // Act (When)
        var description = provider.GetRequiredService<IPipelineDescriber>().Describe<PlainCommand>();

        // Assert (Then)
        Assert.NotNull(description);
        Assert.Empty(description.Behaviors);
    }

    [Fact]
    public void Describe_WithNoHandlerRegistered_ReturnsNull()
    {
        // Arrange (Given)
        var services = new ServiceCollection().AddLogging();
        services.AddSynapse(_ => { });
        using var provider = services.BuildServiceProvider();

        // Act (When)
        var description = provider.GetRequiredService<IPipelineDescriber>().Describe<PlainCommand>();

        // Assert (Then)
        Assert.Null(description);
    }

    [Fact]
    public void Describe_WithAHandlerRegisteredOutsideSynapse_ReportsItWithNoBehaviors()
    {
        // Arrange (Given) — no proxy wraps it, so no behavior can be applied to it
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton(new Trace());
        services.AddSynapse(_ => { });
        services.AddScoped<IRequestHandler<PlainCommand>, PlainCommandHandler>();
        using var provider = services.BuildServiceProvider();

        // Act (When)
        var description = provider.GetRequiredService<IPipelineDescriber>().Describe<PlainCommand>();

        // Assert (Then)
        Assert.NotNull(description);
        Assert.Equal(new[] { typeof(PlainCommandHandler) }, description.Handlers);
        Assert.Empty(description.Behaviors);
    }

    [Fact]
    public void AddSynapse_CalledTwice_RegistersOneSharedDescriber()
    {
        // Arrange (Given)
        var services = new ServiceCollection().AddLogging();

        // Act (When)
        services.AddSynapse(_ => { });
        services.AddSynapse(_ => { });
        using var provider = services.BuildServiceProvider();

        // Assert (Then)
        Assert.Same(provider.GetRequiredService<IPipelineDescriber>(),
            provider.GetRequiredService<IPipelineDescriber>());
    }

    private static ServiceProvider Build(Trace trace, Action<ISynapseConfig> configure)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton(trace);
        services.AddSynapse(cfg =>
        {
            cfg.RegisterRequestHandler<PlainCommandHandler, PlainCommand>();
            cfg.RegisterRequestHandler<CountQueryHandler, CountQuery, int>();
            configure(cfg);
        });
        return services.BuildServiceProvider();
    }

    // One scope per invocation, as one request would have.
    private static async Task Invoke<TRequest>(IServiceProvider provider, TRequest request)
        where TRequest : IRequest
    {
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IInvoker>()
            .InvokeAsync(request, TestContext.Current.CancellationToken);
    }

    private sealed class Trace
    {
        public List<string> Steps { get; } = [];
    }

    private sealed record PlainCommand : IRequest;

    private sealed class PlainCommandHandler(Trace trace) : IRequestHandler<PlainCommand>
    {
        public ValueTask<Result> HandleAsync(PlainCommand request, CancellationToken cancellationToken = default)
        {
            trace.Steps.Add("handler");
            return new ValueTask<Result>(Result.Success());
        }
    }

    private sealed record CountQuery : IRequest<int>;

    private sealed class CountQueryHandler : IRequestHandler<CountQuery, int>
    {
        public ValueTask<Result<int>> HandleAsync(CountQuery request, CancellationToken cancellationToken = default)
        {
            return new ValueTask<Result<int>>(Result.Success(1));
        }
    }

    private sealed class CountBehavior : IRequestPipelineBehavior<CountQuery, int>
    {
        public ValueTask<Result<int>> HandleAsync(CountQuery request,
            RequestHandlerDelegate<CountQuery, int> next,
            CancellationToken cancellationToken = default)
        {
            return next(request, cancellationToken);
        }
    }

    private abstract class TracingBehavior<TRequest>(Trace trace, string name) : IRequestPipelineBehavior<TRequest>
        where TRequest : IRequest
    {
        public ValueTask<Result> HandleAsync(TRequest request,
            RequestHandlerDelegate<TRequest> next,
            CancellationToken cancellationToken = default)
        {
            trace.Steps.Add(name);
            return next(request, cancellationToken);
        }
    }

    private sealed class OuterBehavior<TRequest>(Trace trace) : TracingBehavior<TRequest>(trace, "Outer"),
        IOrderedPipelineBehavior
        where TRequest : IRequest
    {
        public uint Order => 5;
    }

    private sealed class InnerBehavior<TRequest>(Trace trace) : TracingBehavior<TRequest>(trace, "Inner"),
        IOrderedPipelineBehavior
        where TRequest : IRequest
    {
        public uint Order => 20;
    }

    private sealed class UnorderedBehavior<TRequest>(Trace trace) : TracingBehavior<TRequest>(trace, "Unordered")
        where TRequest : IRequest;

    private sealed class FirstTieBehavior<TRequest>(Trace trace) : TracingBehavior<TRequest>(trace, "FirstTie"),
        IOrderedPipelineBehavior
        where TRequest : IRequest
    {
        public uint Order => 10;
    }

    private sealed class SecondTieBehavior<TRequest>(Trace trace) : TracingBehavior<TRequest>(trace, "SecondTie"),
        IOrderedPipelineBehavior
        where TRequest : IRequest
    {
        public uint Order => 10;
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test --project test/Synapse.Tests -f net10.0 --filter-class "*PipelineDescriberTests"`
Expected: build succeeds; the tests that expect a non-null description FAIL with `Assert.NotNull() Failure: Value is null`. `Describe_WithNoHandlerRegistered_ReturnsNull` and `AddSynapse_CalledTwice_RegistersOneSharedDescriber` pass already (they pass against the skeleton, that is expected). If the build fails on an xUnit analyzer error, fix the test, not the analyzer.

- [ ] **Step 5: Implement**

In `src/Synapse/Pipelines/PipelineBehaviorOrdering.cs` add `using UnambitiousFx.Synapse.Abstractions;` is already there; add this method inside the class, after `OrderOf`:

```csharp
    /// <summary>
    ///     Reports already-sorted behaviors as their concrete type and declared order, for
    ///     <see cref="IPipelineDescriber" />.
    /// </summary>
    public static IReadOnlyList<BehaviorDescription> Describe(IReadOnlyList<object> sortedBehaviors)
    {
        var described = new BehaviorDescription[sortedBehaviors.Count];
        for (var i = 0; i < described.Length; i++)
        {
            described[i] = new BehaviorDescription(sortedBehaviors[i].GetType(), OrderOf(sortedBehaviors[i]));
        }

        return described;
    }
```

In `src/Synapse/ProxyRequestHandler.cs`, for BOTH proxy classes: add `, IPipelineInfo` to the interface list, keep the sorted array in a field, and implement the interface explicitly.

No-response proxy (`ProxyRequestHandler<TRequestHandler, TRequest>`):

```csharp
internal sealed class ProxyRequestHandler<TRequestHandler, TRequest>
    : IRequestHandler<TRequest>, IPipelineInfo
    where TRequestHandler : class, IRequestHandler<TRequest>
    where TRequest : IRequest
{
    private readonly IRequestPipelineBehavior<TRequest>[] _behaviors;
    // ...existing _pipeline field and comment stay as they are...

    public ProxyRequestHandler(TRequestHandler handler,
        IEnumerable<IRequestPipelineBehavior<TRequest>> behaviors)
    {
        var sorted = behaviors.OrderBy(PipelineBehaviorOrdering.OrderOf).ToArray();
        _behaviors = sorted;
        // ...the rest of the constructor stays as it is...
    }

    Type IPipelineInfo.HandlerType => typeof(TRequestHandler);

    IReadOnlyList<BehaviorDescription> IPipelineInfo.Behaviors => PipelineBehaviorOrdering.Describe(_behaviors);

    // ...HandleAsync stays as it is...
}
```

With-response proxy (`ProxyRequestHandler<TRequestHandler, TRequest, TResponse>`): identical, with `IRequestPipelineBehavior<TRequest, TResponse>[] _behaviors`.

Replace the body of `PipelineDescriber` (keep the class, constructor and field) with:

```csharp
    public PipelineDescription? Describe<TRequest>()
        where TRequest : IRequest
    {
        using var scope = _scopeFactory.CreateScope();
        return DescribeHandler(scope.ServiceProvider.GetService<IRequestHandler<TRequest>>());
    }

    public PipelineDescription? Describe<TRequest, TResponse>()
        where TRequest : IRequest<TResponse>
        where TResponse : notnull
    {
        using var scope = _scopeFactory.CreateScope();
        return DescribeHandler(scope.ServiceProvider.GetService<IRequestHandler<TRequest, TResponse>>());
    }

    private static PipelineDescription? DescribeHandler(object? handler)
    {
        return handler switch
        {
            null => null,
            IPipelineInfo info => new PipelineDescription([info.HandlerType], info.Behaviors),
            // Registered straight into the container rather than through Synapse: nothing wraps it, so no
            // behavior can apply.
            _ => new PipelineDescription([handler.GetType()], [])
        };
    }
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test --project test/Synapse.Tests -f net10.0 --filter-class "*PipelineDescriberTests"`
Expected: 7 passed. Then run the whole project once for regressions: `dotnet test --project test/Synapse.Tests -f net10.0` — all pass.

- [ ] **Step 7: Commit**

```bash
git add src/Synapse.Abstractions/BehaviorDescription.cs src/Synapse.Abstractions/PipelineDescription.cs \
  src/Synapse.Abstractions/IPipelineDescriber.cs src/Synapse/Pipelines/IPipelineInfo.cs \
  src/Synapse/Pipelines/PipelineDescriber.cs src/Synapse/Pipelines/PipelineBehaviorOrdering.cs \
  src/Synapse/ProxyRequestHandler.cs src/Synapse/DependencyInjectionExtensions.cs \
  test/Synapse.Tests/Pipelines/PipelineDescriberTests.cs
git commit -m "feat(pipelines): describe the resolved pipeline of a request type

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 2: Describe event pipelines

**Files:**
- Create: `src/Synapse/Publish/EventPipelineParts.cs`
- Modify: `src/Synapse/Publish/EventDispatcher.cs` (`BuildPipeline`), `src/Synapse/Pipelines/PipelineDescriber.cs`, `src/Synapse.Abstractions/IPipelineDescriber.cs`
- Test: `test/Synapse.Tests/Pipelines/PipelineDescriberTests.cs` (add tests)

**Interfaces:**
- Consumes: `PipelineBehaviorOrdering.Describe(IReadOnlyList<object>)`, `PipelineBehaviorOrdering.OrderOf(object)`, `IDependencyResolver` (`src/Synapse/Resolvers`, registered scoped by `AddSynapse`), `PipelineDescriber(IServiceScopeFactory)`.
- Produces: `PipelineDescription? IPipelineDescriber.DescribeEvent<TEvent>() where TEvent : class, IEvent`; `internal static (IEventHandler<TEvent>[] Handlers, IEventPipelineBehavior<TEvent>[] Behaviors) EventPipelineParts.Resolve<TEvent>(IDependencyResolver resolver)`.

- [ ] **Step 1: Add the interface method and a skeleton that returns `null`**

Append to `IPipelineDescriber`:

```csharp
    /// <summary>
    ///     Describes the pipeline of an event: the behaviors that wrap the fan-out, and every handler subscribed to it.
    /// </summary>
    /// <typeparam name="TEvent">The event type.</typeparam>
    /// <returns>The description, or <c>null</c> when no handler is subscribed to the event.</returns>
    PipelineDescription? DescribeEvent<TEvent>()
        where TEvent : class, IEvent;
```

Append to `PipelineDescriber` (temporary):

```csharp
    public PipelineDescription? DescribeEvent<TEvent>()
        where TEvent : class, IEvent
    {
        return null;
    }
```

- [ ] **Step 2: Write the failing tests**

Add `using UnambitiousFx.Synapse.Publish;` is NOT needed. Add these tests and helper types to `PipelineDescriberTests` (add them before the `Build` helper / at the end of the class respectively):

```csharp
    [Fact]
    public async Task DescribeEvent_WithBehaviorsAndSeveralHandlers_ListsThemAsTheyExecute()
    {
        // Arrange (Given) — behaviors registered out of order on purpose
        var trace = new Trace();
        await using var provider = BuildEvents(trace, cfg =>
        {
            cfg.RegisterEventPipelineBehavior<InnerEventBehavior, EventExample>();
            cfg.RegisterEventPipelineBehavior<OuterEventBehavior, EventExample>();
            cfg.RegisterEventHandler<FirstEventHandler, EventExample>();
            cfg.RegisterEventHandler<SecondEventHandler, EventExample>();
        });

        // Act (When)
        var description = provider.GetRequiredService<IPipelineDescriber>().DescribeEvent<EventExample>();
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IEventDispatcher>()
                .DispatchAsync(new EventExample("described"), TestContext.Current.CancellationToken);
        }

        // Assert (Then)
        Assert.NotNull(description);
        Assert.Equal(new[] { typeof(FirstEventHandler), typeof(SecondEventHandler) }, description.Handlers);
        Assert.Equal(new[] { typeof(OuterEventBehavior), typeof(InnerEventBehavior) },
            description.Behaviors.Select(behavior => behavior.Type));
        Assert.Equal(new uint[] { 5, 20 }, description.Behaviors.Select(behavior => behavior.Order));
        Assert.Equal(new[] { "Outer", "Inner" },
            trace.Steps.Where(step => !step.StartsWith("handler", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task DescribeEvent_WithNoSubscriber_ReturnsNull()
    {
        // Arrange (Given) — a behavior alone does not make an event handled
        await using var provider = BuildEvents(new Trace(),
            cfg => cfg.RegisterEventPipelineBehavior<OuterEventBehavior, EventExample>());

        // Act (When)
        var description = provider.GetRequiredService<IPipelineDescriber>().DescribeEvent<EventExample>();

        // Assert (Then)
        Assert.Null(description);
    }
```

Helpers, added next to `Build`:

```csharp
    private static ServiceProvider BuildEvents(Trace trace, Action<ISynapseConfig> configure)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton(trace);
        services.AddSynapse(configure);
        return services.BuildServiceProvider();
    }
```

Types, added at the end of the class (add `using UnambitiousFx.Synapse.Tests.Definitions;` at the top for `EventExample`):

```csharp
    private abstract class TracingEventBehavior(Trace trace, string name) : IEventPipelineBehavior<EventExample>
    {
        public ValueTask<Result> HandleAsync(EventExample @event,
            EventHandlerDelegate<EventExample> next,
            CancellationToken cancellationToken = default)
        {
            trace.Steps.Add(name);
            return next(@event, cancellationToken);
        }
    }

    private sealed class OuterEventBehavior(Trace trace) : TracingEventBehavior(trace, "Outer"),
        IOrderedPipelineBehavior
    {
        public uint Order => 5;
    }

    private sealed class InnerEventBehavior(Trace trace) : TracingEventBehavior(trace, "Inner"),
        IOrderedPipelineBehavior
    {
        public uint Order => 20;
    }

    private sealed class FirstEventHandler(Trace trace) : IEventHandler<EventExample>
    {
        public ValueTask<Result> HandleAsync(EventExample @event, CancellationToken cancellationToken = default)
        {
            trace.Steps.Add("handler:first");
            return new ValueTask<Result>(Result.Success());
        }
    }

    private sealed class SecondEventHandler(Trace trace) : IEventHandler<EventExample>
    {
        public ValueTask<Result> HandleAsync(EventExample @event, CancellationToken cancellationToken = default)
        {
            trace.Steps.Add("handler:second");
            return new ValueTask<Result>(Result.Success());
        }
    }
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test --project test/Synapse.Tests -f net10.0 --filter-method "*DescribeEvent*"`
Expected: `DescribeEvent_WithBehaviorsAndSeveralHandlers_...` FAILS with `Assert.NotNull() Failure: Value is null`; `..._WithNoSubscriber_ReturnsNull` passes (skeleton returns null).

- [ ] **Step 4: Implement**

`src/Synapse/Publish/EventPipelineParts.cs`:

```csharp
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Pipelines;
using UnambitiousFx.Synapse.Resolvers;

namespace UnambitiousFx.Synapse.Publish;

/// <summary>
///     Resolves what an event's pipeline is made of. Shared by the dispatcher, which composes it, and the pipeline
///     describer, which reports it, so the two cannot disagree.
/// </summary>
internal static class EventPipelineParts
{
    /// <summary>
    ///     Resolves the handlers subscribed to <typeparamref name="TEvent" /> and the behaviors declared for exactly
    ///     that type, the behaviors ordered by runtime pipeline position. The stable sort keeps registration order
    ///     for behaviors that share an <c>Order</c>.
    /// </summary>
    public static (IEventHandler<TEvent>[] Handlers, IEventPipelineBehavior<TEvent>[] Behaviors) Resolve<TEvent>(
        IDependencyResolver resolver)
        where TEvent : class, IEvent
    {
        var resolved = resolver.GetServices<IEventHandler<TEvent>>();
        var handlers = resolved as IEventHandler<TEvent>[] ?? resolved.ToArray();

        var behaviors = resolver.GetServices<IEventPipelineBehavior<TEvent>>()
            .OrderBy(PipelineBehaviorOrdering.OrderOf)
            .ToArray();

        return (handlers, behaviors);
    }
}
```

In `EventDispatcher.BuildPipeline`, replace the block from `// Behaviors are resolved as IEventPipelineBehavior<TEvent> ...` down to (and including) the `.ToArray();` that ends the `behaviors` declaration — i.e. the `handlers` and `behaviors` declarations and the comments around them — with:

```csharp
        var (handlers, behaviors) = EventPipelineParts.Resolve<TEvent>(_dependencyResolver);
```

Leave the `EventHandlerDelegate<TEvent> next = ...` composition loop untouched.

In `PipelineDescriber`, replace the skeleton `DescribeEvent` with (add `using UnambitiousFx.Synapse.Publish;` and `using UnambitiousFx.Synapse.Resolvers;`):

```csharp
    public PipelineDescription? DescribeEvent<TEvent>()
        where TEvent : class, IEvent
    {
        using var scope = _scopeFactory.CreateScope();
        var (handlers, behaviors) =
            EventPipelineParts.Resolve<TEvent>(scope.ServiceProvider.GetRequiredService<IDependencyResolver>());

        if (handlers.Length == 0)
        {
            return null;
        }

        return new PipelineDescription(
            handlers.Select(handler => handler.GetType()).ToArray(),
            PipelineBehaviorOrdering.Describe(behaviors));
    }
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --project test/Synapse.Tests -f net10.0`
Expected: all pass, including the existing event dispatcher tests (the `BuildPipeline` refactor must not change behavior).

- [ ] **Step 6: Commit**

```bash
git add src/Synapse/Publish/EventPipelineParts.cs src/Synapse/Publish/EventDispatcher.cs \
  src/Synapse/Pipelines/PipelineDescriber.cs src/Synapse.Abstractions/IPipelineDescriber.cs \
  test/Synapse.Tests/Pipelines/PipelineDescriberTests.cs
git commit -m "feat(pipelines): describe the resolved pipeline of an event type

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 3: Non-generic `Describe(Type)` / `DescribeEvent(Type)`

**Files:**
- Modify: `src/Synapse.Abstractions/IPipelineDescriber.cs`, `src/Synapse/Pipelines/PipelineDescriber.cs`
- Test: `test/Synapse.Tests/Pipelines/PipelineDescriberTests.cs` (add tests)

**Interfaces:**
- Consumes: the four generic methods from tasks 1-2.
- Produces: `PipelineDescription? IPipelineDescriber.Describe(Type requestType)` and `PipelineDescription? IPipelineDescriber.DescribeEvent(Type eventType)`, both `[RequiresDynamicCode]` + `[RequiresUnreferencedCode]`; both throw `ArgumentNullException` for `null` and `ArgumentException` for a type that is not a request / event.

- [ ] **Step 1: Add the interface members and skeletons**

Add `using System.Diagnostics.CodeAnalysis;` to `IPipelineDescriber.cs` and append:

```csharp
    /// <summary>
    ///     Describes the pipeline of a request known only as a <see cref="Type" />, for tests that loop over every
    ///     request in an assembly. Picks <see cref="IRequest" /> or <see cref="IRequest{TResponse}" /> from the type.
    /// </summary>
    /// <param name="requestType">A type implementing <see cref="IRequest" /> or <see cref="IRequest{TResponse}" />.</param>
    /// <returns>The description, or <c>null</c> when no handler is registered for the request.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requestType" /> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="requestType" /> is not a request type.</exception>
    /// <remarks>
    ///     Builds a generic method at runtime, so it is not Native-AOT safe. Use the generic overloads in an AOT
    ///     application; this one is meant for tests.
    /// </remarks>
    [RequiresDynamicCode("Builds a generic method over the request type at runtime. Use the generic overloads under Native AOT.")]
    [RequiresUnreferencedCode("Looks up the generic overloads by reflection. Use the generic overloads when trimming.")]
    PipelineDescription? Describe(Type requestType);

    /// <summary>
    ///     Describes the pipeline of an event known only as a <see cref="Type" />. See <see cref="Describe(Type)" />.
    /// </summary>
    /// <param name="eventType">A reference type implementing <see cref="IEvent" />.</param>
    /// <returns>The description, or <c>null</c> when no handler is subscribed to the event.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="eventType" /> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="eventType" /> is not an event type.</exception>
    [RequiresDynamicCode("Builds a generic method over the event type at runtime. Use the generic overloads under Native AOT.")]
    [RequiresUnreferencedCode("Looks up the generic overloads by reflection. Use the generic overloads when trimming.")]
    PipelineDescription? DescribeEvent(Type eventType);
```

Append to `PipelineDescriber` (add `using System.Diagnostics.CodeAnalysis;`):

```csharp
    [RequiresDynamicCode("Builds a generic method over the request type at runtime. Use the generic overloads under Native AOT.")]
    [RequiresUnreferencedCode("Looks up the generic overloads by reflection. Use the generic overloads when trimming.")]
    public PipelineDescription? Describe(Type requestType)
    {
        throw new NotImplementedException();
    }

    [RequiresDynamicCode("Builds a generic method over the event type at runtime. Use the generic overloads under Native AOT.")]
    [RequiresUnreferencedCode("Looks up the generic overloads by reflection. Use the generic overloads when trimming.")]
    public PipelineDescription? DescribeEvent(Type eventType)
    {
        throw new NotImplementedException();
    }
```

- [ ] **Step 2: Write the failing tests**

Add to `PipelineDescriberTests`:

```csharp
    [Fact]
    public async Task Describe_WithARequestType_MatchesTheGenericOverload()
    {
        // Arrange (Given)
        await using var provider = Build(new Trace(),
            cfg => cfg.RegisterRequestPipelineBehavior<OuterBehavior<PlainCommand>, PlainCommand>());
        var describer = provider.GetRequiredService<IPipelineDescriber>();

        // Act (When)
        var byType = describer.Describe(typeof(PlainCommand));
        var generic = describer.Describe<PlainCommand>();

        // Assert (Then)
        AssertSameDescription(generic, byType);
    }

    [Fact]
    public async Task Describe_WithARequestTypeThatHasAResponse_MatchesTheGenericOverload()
    {
        // Arrange (Given)
        await using var provider = Build(new Trace(),
            cfg => cfg.RegisterRequestPipelineBehavior<CountBehavior, CountQuery, int>());
        var describer = provider.GetRequiredService<IPipelineDescriber>();

        // Act (When)
        var byType = describer.Describe(typeof(CountQuery));
        var generic = describer.Describe<CountQuery, int>();

        // Assert (Then)
        AssertSameDescription(generic, byType);
    }

    [Fact]
    public async Task DescribeEvent_WithAnEventType_MatchesTheGenericOverload()
    {
        // Arrange (Given)
        await using var provider = BuildEvents(new Trace(), cfg =>
        {
            cfg.RegisterEventPipelineBehavior<OuterEventBehavior, EventExample>();
            cfg.RegisterEventHandler<FirstEventHandler, EventExample>();
        });
        var describer = provider.GetRequiredService<IPipelineDescriber>();

        // Act (When)
        var byType = describer.DescribeEvent(typeof(EventExample));
        var generic = describer.DescribeEvent<EventExample>();

        // Assert (Then)
        AssertSameDescription(generic, byType);
    }

    [Fact]
    public async Task Describe_WithATypeThatIsNotARequest_ThrowsArgumentException()
    {
        // Arrange (Given)
        await using var provider = Build(new Trace(), _ => { });
        var describer = provider.GetRequiredService<IPipelineDescriber>();

        // Act (When)
        var exception = Record.Exception(() => describer.Describe(typeof(string)));

        // Assert (Then)
        Assert.IsType<ArgumentException>(exception);
    }

    [Fact]
    public async Task DescribeEvent_WithATypeThatIsNotAnEvent_ThrowsArgumentException()
    {
        // Arrange (Given)
        await using var provider = Build(new Trace(), _ => { });
        var describer = provider.GetRequiredService<IPipelineDescriber>();

        // Act (When)
        var exception = Record.Exception(() => describer.DescribeEvent(typeof(string)));

        // Assert (Then)
        Assert.IsType<ArgumentException>(exception);
    }

    [Fact]
    public async Task Describe_WithANullType_ThrowsArgumentNullException()
    {
        // Arrange (Given)
        await using var provider = Build(new Trace(), _ => { });
        var describer = provider.GetRequiredService<IPipelineDescriber>();

        // Act (When)
        var exception = Record.Exception(() => describer.Describe(null!));

        // Assert (Then)
        Assert.IsType<ArgumentNullException>(exception);
    }

    private static void AssertSameDescription(PipelineDescription? expected, PipelineDescription? actual)
    {
        Assert.NotNull(expected);
        Assert.NotNull(actual);
        Assert.Equal(expected.Handlers, actual.Handlers);
        Assert.Equal(expected.Behaviors, actual.Behaviors);
    }
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test --project test/Synapse.Tests -f net10.0 --filter-class "*PipelineDescriberTests"`
Expected: the six new tests FAIL with `NotImplementedException` (or, for the two exception-type tests, `Assert.IsType() Failure` showing `NotImplementedException`).

- [ ] **Step 4: Implement**

Replace the two skeleton methods in `PipelineDescriber` with:

```csharp
    [RequiresDynamicCode("Builds a generic method over the request type at runtime. Use the generic overloads under Native AOT.")]
    [RequiresUnreferencedCode("Looks up the generic overloads by reflection. Use the generic overloads when trimming.")]
    public PipelineDescription? Describe(Type requestType)
    {
        ArgumentNullException.ThrowIfNull(requestType);

        var responseType = requestType.GetInterfaces()
            .Where(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IRequest<>))
            .Select(candidate => candidate.GetGenericArguments()[0])
            .FirstOrDefault();

        if (responseType is not null)
        {
            return InvokeGeneric(nameof(Describe), requestType, responseType);
        }

        if (typeof(IRequest).IsAssignableFrom(requestType))
        {
            return InvokeGeneric(nameof(Describe), requestType);
        }

        throw new ArgumentException(
            $"'{requestType}' does not implement IRequest or IRequest<TResponse>.", nameof(requestType));
    }

    [RequiresDynamicCode("Builds a generic method over the event type at runtime. Use the generic overloads under Native AOT.")]
    [RequiresUnreferencedCode("Looks up the generic overloads by reflection. Use the generic overloads when trimming.")]
    public PipelineDescription? DescribeEvent(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);

        if (eventType.IsValueType || !typeof(IEvent).IsAssignableFrom(eventType))
        {
            throw new ArgumentException($"'{eventType}' is not a reference type implementing IEvent.",
                nameof(eventType));
        }

        return InvokeGeneric(nameof(DescribeEvent), eventType);
    }

    // The generic overload is picked by name and arity, since Describe(Type) shares its name with them.
    [RequiresDynamicCode("Builds a generic method at runtime.")]
    [RequiresUnreferencedCode("Looks up the generic overloads by reflection.")]
    private PipelineDescription? InvokeGeneric(string name, params Type[] typeArguments)
    {
        var method = typeof(PipelineDescriber)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(candidate => candidate.Name == name &&
                                 candidate.IsGenericMethodDefinition &&
                                 candidate.GetGenericArguments().Length == typeArguments.Length)
            .MakeGenericMethod(typeArguments);

        // DoNotWrapExceptions so a throwing behavior constructor surfaces as itself, not as a TargetInvocationException.
        return (PipelineDescription?)method.Invoke(this, BindingFlags.DoNotWrapExceptions, null, null, null);
    }
```

Add `using System.Reflection;` at the top of `PipelineDescriber.cs`.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --project test/Synapse.Tests -f net10.0`
Expected: all pass. Then confirm the library still builds warning-free for every target: `dotnet build src/Synapse -c Release` — expect `0 Warning(s)`. If an IL2026/IL3050 warning appears on `InvokeGeneric` or the two public methods, the attribute messages above are the fix; do not suppress.

- [ ] **Step 6: Commit**

```bash
git add src/Synapse.Abstractions/IPipelineDescriber.cs src/Synapse/Pipelines/PipelineDescriber.cs \
  test/Synapse.Tests/Pipelines/PipelineDescriberTests.cs
git commit -m "feat(pipelines): describe a pipeline from a runtime Type, for architecture tests

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 4: Exercise it under Native AOT, and document it

**Files:**
- Modify: `examples/MinimalApi/Program.cs`, `.github/workflows/ci.yml`, `docs/docs/pipelines.mdx`
- Create: `examples/MinimalApi.Tests/PipelinesApiTests.cs`

**Interfaces:**
- Consumes: `IPipelineDescriber.Describe<TRequest, TResponse>()`, `CreateTaskCommand : IRequest<CreateTaskResult>` (`examples/MinimalApi/Features/Tasks/Commands.cs`), `IPipelineDescriber.Describe(Type)` (docs only).
- Produces: `GET /pipelines/create-task` returning plain text, one behavior per line, outermost first, formatted `<Order> <TypeName>`.

- [ ] **Step 1: Write the failing endpoint test**

`examples/MinimalApi.Tests/PipelinesApiTests.cs`:

```csharp
using Microsoft.AspNetCore.Mvc.Testing;

namespace UnambitiousFx.Examples.MinimalApi.Tests;

public sealed class PipelinesApiTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PipelinesApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetCreateTaskPipeline_ListsTheCqrsBoundaryBehaviorOutermost()
    {
        // Arrange (Given)
        var client = _factory.CreateClient();

        // Act (When)
        var body = await client.GetStringAsync("/pipelines/create-task", TestContext.Current.CancellationToken);

        // Assert (Then)
        var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("CqrsBoundaryEnforcementBehavior", lines[0], StringComparison.Ordinal);
        Assert.Contains(lines, line => line.Contains("AuthorizationBehavior", StringComparison.Ordinal));
    }
}
```

Run: `dotnet test --project examples/MinimalApi.Tests -f net10.0`
Expected: FAIL (`404` → `HttpRequestException`), the route does not exist yet. If the class name of the authorization behavior differs, run `grep -rn "class .*Authorization" examples/MinimalApi` and use that name.

- [ ] **Step 2: Add the endpoint**

In `examples/MinimalApi/Program.cs`, directly after the `app.MapGet("/", ...)` line, add:

```csharp
// ── Pipeline inspection ───────────────────────────────────────────────
// IPipelineDescriber's generic overloads are Native-AOT safe, so this endpoint also exercises them under the
// AOT CI job. Plain text: an anonymous JSON shape would need a source-generated serializer context.
app.MapGet("/pipelines/create-task", ([FromServices] IPipelineDescriber describer) =>
{
    var description = describer.Describe<CreateTaskCommand, CreateTaskResult>();
    return description is null
        ? Results.NotFound()
        : Results.Text(string.Join('\n', description.Behaviors.Select(b => $"{b.Order} {b.Type.Name}")));
});
```

`CreateTaskCommand`/`CreateTaskResult` live in `UnambitiousFx.Examples.MinimalApi.Features.Tasks` (already imported), `IPipelineDescriber` in `UnambitiousFx.Synapse.Abstractions` (already imported).

- [ ] **Step 3: Run the endpoint test to verify it passes**

Run: `dotnet test --project examples/MinimalApi.Tests -f net10.0`
Expected: PASS.

- [ ] **Step 4: Make the AOT CI job hit it**

In `.github/workflows/ci.yml`, in the step `Test AOT binary starts`, replace the success branch

```yaml
          if [ "$started" = true ]; then
            echo "✓ Native AOT application started successfully"
          else
```

with

```yaml
          if [ "$started" = true ]; then
            echo "✓ Native AOT application started successfully"
            pipeline=$(curl -fsS "http://localhost:${port}/pipelines/create-task")
            echo "$pipeline"
            if ! grep -q CqrsBoundaryEnforcementBehavior <<< "$pipeline"; then
              echo "✗ IPipelineDescriber did not report the expected pipeline under Native AOT"
              exit 1
            fi
            echo "✓ IPipelineDescriber works under Native AOT"
          else
```

(`port` still holds the value it had when the loop broke. Use a heredoc-free `grep -q ... <<<` on purpose: an earlier commit, `25c49bc`, fixed a bash-quote clash in this file, so keep quoting minimal.)

- [ ] **Step 5: Document it**

Append to `docs/docs/pipelines.mdx`:

````markdown
## Inspecting a pipeline

`IPipelineDescriber` reports which handler(s) and behaviors a request or event type resolves to, in the order they execute. Use it in an architecture test to prove a behavior can never be skipped:

```csharp
var describer = provider.GetRequiredService<IPipelineDescriber>();

var pipeline = describer.Describe<CreateTaskCommand, CreateTaskResult>();

Assert.NotNull(pipeline);
Assert.Equal(typeof(CreateTaskHandler), Assert.Single(pipeline.Handlers));
Assert.Contains(pipeline.Behaviors, b => b.Type.GetGenericTypeDefinition() == typeof(AuthorizationBehavior<,>));
```

`Behaviors` is outermost first; each entry carries the concrete behavior type and the `Order` it declared (`IOrderedPipelineBehavior.Last` when it declares none). Behaviors that share an `Order` keep their registration order. `Describe` returns `null` when nothing handles the message.

To check every request in an assembly, use the non-generic overloads:

```csharp
foreach (var requestType in typeof(CreateTaskCommand).Assembly.GetTypes().Where(IsRequest))
{
    var pipeline = describer.Describe(requestType);
    Assert.Contains(pipeline!.Behaviors, b => b.Type.Name.StartsWith("AuthorizationBehavior", StringComparison.Ordinal));
}
```

:::note
`Describe(Type)` and `DescribeEvent(Type)` build a generic method at runtime, so they are not Native-AOT safe: use them in tests, and the generic overloads (which are AOT safe) in an application.
:::

:::caution
Describing resolves the behaviors from a fresh DI scope, because `Order` is an instance property. A behavior whose constructor needs something that only exists inside a real request (for example an `HttpContext`) can throw there, and a handler that only implements `IAsyncDisposable` makes disposing that scope throw.
:::
````

- [ ] **Step 6: Verify everything, including the docs build**

Run, in order:
1. `dotnet test --solution Synapse.slnx` — expect all pass, 0 failed.
2. `dotnet build src/Synapse -c Release` — expect `0 Warning(s)` (AOT/trim analyzers).
3. `(cd docs && pnpm build)` — expect `[SUCCESS]`, no broken links.
4. Best effort, if a native toolchain is installed: `dotnet publish examples/MinimalApi/MinimalApi.csproj -c Release -r osx-arm64 --self-contained`, run the binary, `curl localhost:5000/pipelines/create-task`. If the toolchain is missing, skip: the CI `Native AOT Validation` job is the authority.

- [ ] **Step 7: Commit**

```bash
git add examples/MinimalApi/Program.cs examples/MinimalApi.Tests/PipelinesApiTests.cs \
  .github/workflows/ci.yml docs/docs/pipelines.mdx
git commit -m "feat(pipelines): exercise IPipelineDescriber under Native AOT and document it

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 5: Pull request, CI, merge

**Files:** none.

- [ ] **Step 1: Push and open the PR**

```bash
git push -u origin feat/pipeline-describer
gh pr create --base main --title "feat(pipelines): IPipelineDescriber to inspect a resolved pipeline" --body "..."
```

The body must: start with `Closes #101.` and `Refs #96.`; summarise the public API; state that the description is read from the proxy that runs the chain (no second computation); state that `Describe(Type)` / `DescribeEvent(Type)` are `[RequiresDynamicCode]` and the generic overloads are AOT-safe and exercised by `GET /pipelines/create-task` in the AOT CI job; note the two caveats (behaviors are instantiated; `IAsyncDisposable`-only handlers throw on scope disposal); note the perf footprint (each request proxy keeps one extra array reference, no dispatch-path change, so no benchmark); end with `🤖 Generated with [Claude Code](https://claude.com/claude-code)`.

- [ ] **Step 2: Wait for CI, then squash-merge**

Run: `gh pr checks <n> --watch --interval 20` and require every check green (including `Native AOT Validation`). Then `gh pr merge <n> --squash --delete-branch`. If a check fails, read the log (`gh run view --log-failed`), fix on the branch, push, and wait again. Never merge red.

- [ ] **Step 3: Close out**

Confirm `gh issue view 101 --json state` is `CLOSED`; add a short comment on #101 summarising what shipped and pointing at the PR. Leave #96 open (#102 and #103 remain), then `git switch main && git pull --ff-only`.
