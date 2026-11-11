using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Abstractions.Exceptions;
using UnambitiousFx.Synapse.Pipelines;

namespace UnambitiousFx.Synapse.Tests.Pipelines;

/// <summary>
///     Relative ordering (#162): stages, Before/After constraints, and the guarantee that Order-only pipelines keep
///     the order they always had.
/// </summary>
[TestSubject(typeof(PipelineOrdering))]
public sealed class PipelineStageOrderingTests
{
    // ── Backward compatibility ───────────────────────────────────────────────

    [Fact]
    public async Task Invoke_WithOrderOnlyBehaviorsAndRulesForOthers_KeepsTheLegacyOrder()
    {
        // Arrange (Given) — a rule exists for an unrelated behavior, so the resolver (not the plain sort) runs.
        // Ties at Order 10 must keep registration order, exactly like the plain stable sort.
        var log = new Log();
        await using var provider = Build(log, cfg =>
        {
            cfg.RegisterRequestHandler<CommandHandler, Command>();
            cfg.RegisterRequestPipelineBehavior<Unordered, Command>();
            cfg.RegisterRequestPipelineBehavior<Order10B, Command>();
            cfg.RegisterRequestPipelineBehavior<OrderMiddle, Command>();
            cfg.RegisterRequestPipelineBehavior<Order10A, Command>();
            cfg.RegisterRequestPipelineBehavior<OrderFirst, Command>();
            cfg.ConfigureBehaviorOrdering(typeof(StagedAuthorization), PipelineStage.Authorization);
        });

        // Act (When)
        await Invoke(provider, new Command());

        // Assert (Then)
        Assert.Equal(["OrderFirst", "Order10B", "Order10A", "OrderMiddle", "Unordered"], log.Steps);
    }

    [Fact]
    public void Sort_WithOrderOnlyBehaviors_MatchesThePlainOrderSort()
    {
        // Arrange (Given)
        var behaviors = new object[]
        {
            new Unordered(new Log()), new Order10B(new Log()), new OrderMiddle(new Log()), new Order10A(new Log()),
            new OrderFirst(new Log()), new OrderInValidationBand(new Log())
        };
        var withRules = new PipelineOrdering([
            BehaviorOrderingRule.Create(typeof(StagedAuthorization), PipelineStage.Authorization, null, null)
        ]);

        // Act (When)
        var resolved = withRules.Sort(typeof(PipelineStageOrderingTests), behaviors, behavior => behavior);
        var withoutRules = new PipelineOrdering([]).Sort(typeof(PipelineStageOrderingTests), behaviors,
            behavior => behavior);
        var legacy = behaviors.OrderBy(PipelineBehaviorOrdering.OrderOf).ToArray();

        // Assert (Then)
        Assert.Equal(legacy, resolved);
        Assert.Equal(legacy, withoutRules);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Sort_WithRandomOrdersAndNoDeclarations_MatchesTheStableOrderSort(int seed)
    {
        // Arrange (Given) — few distinct Orders so ties are frequent, spread across every stage band.
        var random = new Random(seed);
        var candidates = new uint[] { 0, 10, 10, 0x2000_0000, 0x6000_0000, uint.MaxValue / 2, uint.MaxValue };
        var behaviors = Enumerable.Range(0, 12)
            .Select(_ => (object)new VariableOrder(candidates[random.Next(candidates.Length)]))
            .ToArray();

        // Act (When)
        var resolved = new PipelineOrdering([]).Sort(typeof(VariableOrder), behaviors, behavior => behavior);
        var legacy = behaviors.OrderBy(PipelineBehaviorOrdering.OrderOf).ToArray();

        // Assert (Then) — reference equality, so tie order is checked too.
        Assert.Equal(legacy, resolved, ReferenceEqualityComparer.Instance);
    }

    // ── Stages ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Invoke_WithDeclaredStages_GroupsBehaviorsByStageWhateverTheirOrder()
    {
        // Arrange (Given) — Order says the authorization behavior is outermost, but stages win.
        var log = new Log();
        await using var provider = Build(log, cfg =>
        {
            cfg.RegisterRequestHandler<CommandHandler, Command>();
            cfg.RegisterRequestPipelineBehavior<StagedValidation, Command>();
            cfg.RegisterRequestPipelineBehavior<StagedAuthorization, Command>();
            cfg.RegisterRequestPipelineBehavior<StagedObservability, Command>();
            cfg.ConfigureBehaviorOrdering(typeof(StagedValidation), PipelineStage.Validation);
            cfg.ConfigureBehaviorOrdering(typeof(StagedAuthorization), PipelineStage.Authorization);
            cfg.ConfigureBehaviorOrdering(typeof(StagedObservability), PipelineStage.Observability);
        });

        // Act (When)
        await Invoke(provider, new Command());

        // Assert (Then)
        Assert.Equal(["StagedObservability", "StagedAuthorization", "StagedValidation"], log.Steps);
    }

    [Fact]
    public async Task Invoke_WithAnUnstagedBehavior_PlacesItInTheStageItsOrderBandImplies()
    {
        // Arrange (Given) — Order 10 is in the Observability band, Middle in the Validation band, so the
        // Authorization-staged behavior lands between them.
        var log = new Log();
        await using var provider = Build(log, cfg =>
        {
            cfg.RegisterRequestHandler<CommandHandler, Command>();
            cfg.RegisterRequestPipelineBehavior<OrderMiddle, Command>();
            cfg.RegisterRequestPipelineBehavior<StagedAuthorization, Command>();
            cfg.RegisterRequestPipelineBehavior<Order10A, Command>();
            cfg.ConfigureBehaviorOrdering(typeof(StagedAuthorization), PipelineStage.Authorization);
        });

        // Act (When)
        await Invoke(provider, new Command());

        // Assert (Then)
        Assert.Equal(["Order10A", "StagedAuthorization", "OrderMiddle"], log.Steps);
    }

    [Fact]
    public async Task Invoke_WithBehaviorsInTheSameStage_OrdersThemByOrderThenRegistration()
    {
        // Arrange (Given)
        var log = new Log();
        await using var provider = Build(log, cfg =>
        {
            cfg.RegisterRequestHandler<CommandHandler, Command>();
            cfg.RegisterRequestPipelineBehavior<Unordered, Command>();
            cfg.RegisterRequestPipelineBehavior<StagedValidation, Command>();
            cfg.RegisterRequestPipelineBehavior<Order10A, Command>();
            cfg.ConfigureBehaviorOrdering(typeof(Unordered), PipelineStage.Validation);
            cfg.ConfigureBehaviorOrdering(typeof(StagedValidation), PipelineStage.Validation);
            cfg.ConfigureBehaviorOrdering(typeof(Order10A), PipelineStage.Validation);
        });

        // Act (When) — Order10A has the lowest Order; the two without Order tie at Last and keep registration order.
        await Invoke(provider, new Command());

        // Assert (Then)
        Assert.Equal(["Order10A", "Unordered", "StagedValidation"], log.Steps);
    }

    // ── Before / After ───────────────────────────────────────────────────────

    [Fact]
    public async Task Invoke_WithBeforeAndAfter_OverridesOrderWithinAStage()
    {
        // Arrange (Given) — by Order: OrderFirst, Order10A, Order10B. Constraints: Order10B before OrderFirst,
        // Order10A after Unordered.
        var log = new Log();
        await using var provider = Build(log, cfg =>
        {
            cfg.RegisterRequestHandler<CommandHandler, Command>();
            cfg.RegisterRequestPipelineBehavior<OrderFirst, Command>();
            cfg.RegisterRequestPipelineBehavior<Order10A, Command>();
            cfg.RegisterRequestPipelineBehavior<Order10B, Command>();
            cfg.RegisterRequestPipelineBehavior<Unordered, Command>();
            cfg.ConfigureBehaviorOrdering(typeof(Order10B), before: typeof(OrderFirst));
            cfg.ConfigureBehaviorOrdering(typeof(Unordered), PipelineStage.Observability, before: typeof(Order10A));
        });

        // Act (When)
        await Invoke(provider, new Command());

        // Assert (Then)
        Assert.Equal(["Order10B", "OrderFirst", "Unordered", "Order10A"], log.Steps);
    }

    [Fact]
    public async Task Invoke_WithAConstraintOnAnAbsentBehavior_IgnoresIt()
    {
        // Arrange (Given)
        var log = new Log();
        await using var provider = Build(log, cfg =>
        {
            cfg.RegisterRequestHandler<CommandHandler, Command>();
            cfg.RegisterRequestPipelineBehavior<Order10A, Command>();
            cfg.RegisterRequestPipelineBehavior<OrderFirst, Command>();
            cfg.ConfigureBehaviorOrdering(typeof(Order10A), after: typeof(StagedValidation));
        });

        // Act (When)
        await Invoke(provider, new Command());

        // Assert (Then)
        Assert.Equal(["OrderFirst", "Order10A"], log.Steps);
    }

    [Fact]
    public async Task Invoke_WithOpenGenericConstraints_MatchesEveryClosedForm()
    {
        // Arrange (Given) — Audit<,> must run after Actor<,> though its Order is lower; Actor<,> is registered as an
        // open generic, Audit<,> as a closed form.
        var log = new Log();
        await using var provider = Build(log, cfg =>
        {
            cfg.RegisterRequestHandler<QueryHandler, Query, int>();
            cfg.RegisterRequestPipelineBehavior<AuditBehavior<Query, int>, Query, int>();
            cfg.AddOpenGenericRequestWithResponsePipelineBehavior(typeof(ActorBehavior<,>));
            cfg.ConfigureBehaviorOrdering(typeof(AuditBehavior<,>), PipelineStage.Authorization,
                after: typeof(ActorBehavior<,>));
            cfg.ConfigureBehaviorOrdering(typeof(ActorBehavior<,>), PipelineStage.Authorization);
        });

        // Act (When)
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IInvoker>()
                .InvokeAsync(new Query(), TestContext.Current.CancellationToken);
        }

        // Assert (Then)
        Assert.Equal(["Actor", "Audit"], log.Steps);
    }

    [Fact]
    public async Task Invoke_WithAClosedRuleAndAnOpenGenericRule_AppliesBoth()
    {
        // Arrange (Given) — the open rule (as the generator emits it) gives the stage; the closed rule (as an
        // application adds it) gives a constraint. Audit has Order First, so only the merged rule puts it after Actor.
        var log = new Log();
        await using var provider = Build(log, cfg =>
        {
            cfg.RegisterRequestHandler<QueryHandler, Query, int>();
            cfg.RegisterRequestPipelineBehavior<AuditBehavior<Query, int>, Query, int>();
            cfg.RegisterRequestPipelineBehavior<ActorBehavior<Query, int>, Query, int>();
            cfg.RegisterRequestPipelineBehavior<Order10QueryBehavior, Query, int>();
            cfg.ConfigureBehaviorOrdering(typeof(AuditBehavior<,>), PipelineStage.Authorization);
            cfg.ConfigureBehaviorOrdering(typeof(ActorBehavior<,>), PipelineStage.Authorization);
            cfg.ConfigureBehaviorOrdering(typeof(AuditBehavior<Query, int>), after: typeof(ActorBehavior<Query, int>));
        });

        // Act (When)
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IInvoker>()
                .InvokeAsync(new Query(), TestContext.Current.CancellationToken);
        }

        var description = provider.GetRequiredService<IPipelineDescriber>().Describe<Query, int>();

        // Assert (Then) — Order10 is Observability, so it still wraps both Authorization behaviors.
        Assert.Equal(["Order10", "Actor", "Audit"], log.Steps);
        Assert.NotNull(description);
        Assert.Equal(PipelineStage.Authorization,
            description.Behaviors.Single(behavior => behavior.Type == typeof(AuditBehavior<Query, int>)).Stage);
    }

    [Fact]
    public void ValidateSynapse_WithAClosedRuleAndAnOpenRuleInDifferentStages_ReportsSyn006()
    {
        // Arrange (Given)
        using var provider = BuildSync(cfg =>
        {
            cfg.RegisterRequestHandler<QueryHandler, Query, int>();
            cfg.RegisterRequestPipelineBehavior<AuditBehavior<Query, int>, Query, int>();
            cfg.ConfigureBehaviorOrdering(typeof(AuditBehavior<,>), PipelineStage.Authorization);
            cfg.ConfigureBehaviorOrdering(typeof(AuditBehavior<Query, int>), PipelineStage.Validation);
        });

        // Act (When)
        var report = provider.ValidateSynapse();

        // Assert (Then)
        var issue = Assert.Single(report.Errors);
        Assert.Equal("SYN006", issue.Code);
        Assert.Contains("Validation stage", issue.Message, StringComparison.Ordinal);
        Assert.Contains("Authorization", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Publish_WithStagedEventBehaviors_OrdersThemByStage()
    {
        // Arrange (Given)
        var log = new Log();
        await using var provider = Build(log, cfg =>
        {
            cfg.RegisterEventHandler<NoticeHandler, Notice>();
            cfg.RegisterEventPipelineBehavior<InnerEventBehavior, Notice>();
            cfg.RegisterEventPipelineBehavior<OuterEventBehavior, Notice>();
            cfg.ConfigureBehaviorOrdering(typeof(OuterEventBehavior), before: typeof(InnerEventBehavior));
        });

        // Act (When)
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IEventDispatcher>()
                .DispatchAsync(new Notice(), TestContext.Current.CancellationToken);
        }

        // Assert (Then)
        Assert.Equal(["OuterEvent", "InnerEvent"], log.Steps);
    }

    [Fact]
    public async Task Stream_WithAnAfterConstraint_OrdersTheStreamBehaviors()
    {
        // Arrange (Given)
        var log = new Log();
        await using var provider = Build(log, cfg =>
        {
            cfg.RegisterStreamRequestHandler<CountHandler, Count, int>();
            cfg.RegisterStreamRequestPipelineBehavior<InnerStreamBehavior, Count, int>();
            cfg.RegisterStreamRequestPipelineBehavior<OuterStreamBehavior, Count, int>();
            cfg.ConfigureBehaviorOrdering(typeof(InnerStreamBehavior), after: typeof(OuterStreamBehavior));
        });

        // Act (When)
        await using (var scope = provider.CreateAsyncScope())
        {
            await foreach (var _ in scope.ServiceProvider.GetRequiredService<IInvoker>()
                               .InvokeStreamAsync(new Count(), TestContext.Current.CancellationToken))
            {
            }
        }

        // Assert (Then)
        Assert.Equal(["OuterStream", "InnerStream"], log.Steps);
    }

    [Fact]
    public async Task Invoke_InSeveralScopes_ReusesTheResolvedOrder()
    {
        // Arrange (Given)
        var log = new Log();
        await using var provider = Build(log, cfg =>
        {
            cfg.RegisterRequestHandler<CommandHandler, Command>();
            cfg.RegisterRequestPipelineBehavior<OrderFirst, Command>();
            cfg.RegisterRequestPipelineBehavior<Order10A, Command>();
            cfg.ConfigureBehaviorOrdering(typeof(Order10A), before: typeof(OrderFirst));
        });

        // Act (When)
        await Invoke(provider, new Command());
        await Invoke(provider, new Command());

        // Assert (Then)
        Assert.Equal(["Order10A", "OrderFirst", "Order10A", "OrderFirst"], log.Steps);
    }

    // ── Conflicts ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Invoke_WithACycle_ThrowsPipelineOrderingException()
    {
        // Arrange (Given)
        await using var provider = Build(new Log(), cfg =>
        {
            cfg.RegisterRequestHandler<CommandHandler, Command>();
            cfg.RegisterRequestPipelineBehavior<Order10A, Command>();
            cfg.RegisterRequestPipelineBehavior<Order10B, Command>();
            cfg.ConfigureBehaviorOrdering(typeof(Order10A), before: typeof(Order10B));
            cfg.ConfigureBehaviorOrdering(typeof(Order10B), before: typeof(Order10A));
        });

        // Act (When)
        var exception = await Assert.ThrowsAsync<PipelineOrderingException>(() => Invoke(provider, new Command()));

        // Assert (Then)
        Assert.True(exception.IsCycle);
        Assert.Equal([typeof(Order10A), typeof(Order10B)], exception.BehaviorTypes);
    }

    [Fact]
    public void ValidateSynapse_WithACycle_ReportsSyn005()
    {
        // Arrange (Given)
        using var provider = BuildSync(cfg =>
        {
            cfg.RegisterRequestHandler<CommandHandler, Command>();
            cfg.RegisterRequestPipelineBehavior<Order10A, Command>();
            cfg.RegisterRequestPipelineBehavior<Order10B, Command>();
            cfg.ConfigureBehaviorOrdering(typeof(Order10A), after: typeof(Order10B));
            cfg.ConfigureBehaviorOrdering(typeof(Order10B), after: typeof(Order10A));
        });

        // Act (When)
        var report = provider.ValidateSynapse();

        // Assert (Then)
        var issue = Assert.Single(report.Errors);
        Assert.Equal("SYN005", issue.Code);
        Assert.Equal(typeof(Command), issue.Type);
        Assert.Contains("cycle", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateSynapse_WithAConstraintThatContradictsTheStages_ReportsSyn006()
    {
        // Arrange (Given) — the Validation behavior cannot run before an Observability one.
        using var provider = BuildSync(cfg =>
        {
            cfg.RegisterRequestHandler<CommandHandler, Command>();
            cfg.RegisterRequestPipelineBehavior<StagedValidation, Command>();
            cfg.RegisterRequestPipelineBehavior<StagedObservability, Command>();
            cfg.ConfigureBehaviorOrdering(typeof(StagedValidation), PipelineStage.Validation,
                before: typeof(StagedObservability));
            cfg.ConfigureBehaviorOrdering(typeof(StagedObservability), PipelineStage.Observability);
        });

        // Act (When)
        var report = provider.ValidateSynapse();

        // Assert (Then)
        var issue = Assert.Single(report.Errors);
        Assert.Equal("SYN006", issue.Code);
        Assert.Contains("Validation stage, after Observability", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateSynapse_WithABehaviorDeclaredInTwoStages_ReportsSyn006()
    {
        // Arrange (Given)
        using var provider = BuildSync(cfg =>
        {
            cfg.RegisterRequestHandler<CommandHandler, Command>();
            cfg.RegisterRequestPipelineBehavior<StagedValidation, Command>();
            cfg.ConfigureBehaviorOrdering(typeof(StagedValidation), PipelineStage.Validation);
            cfg.ConfigureBehaviorOrdering(typeof(StagedValidation), PipelineStage.Transaction);
        });

        // Act (When)
        var report = provider.ValidateSynapse();

        // Assert (Then)
        var issue = Assert.Single(report.Errors);
        Assert.Equal("SYN006", issue.Code);
        Assert.Equal(typeof(StagedValidation), issue.Type);
    }

    [Fact]
    public void ValidateSynapse_WithATieSettledByAConstraint_DoesNotReportSyn004()
    {
        // Arrange (Given) — Order10A and Order10B share Order 10, but a constraint orders them.
        using var provider = BuildSync(cfg =>
        {
            cfg.RegisterRequestHandler<CommandHandler, Command>();
            cfg.RegisterRequestPipelineBehavior<Order10A, Command>();
            cfg.RegisterRequestPipelineBehavior<Order10B, Command>();
            cfg.ConfigureBehaviorOrdering(typeof(Order10B), before: typeof(Order10A));
        });

        // Act (When)
        var report = provider.ValidateSynapse();

        // Assert (Then)
        Assert.Empty(report.Issues);
    }

    [Fact]
    public void ValidateSynapse_WithATieSettledByAChainOfConstraints_DoesNotReportSyn004()
    {
        // Arrange (Given) — three behaviors share Order 10; A before B and B before C also order A and C.
        using var provider = BuildSync(cfg =>
        {
            cfg.RegisterRequestHandler<CommandHandler, Command>();
            cfg.RegisterRequestPipelineBehavior<Order10C, Command>();
            cfg.RegisterRequestPipelineBehavior<Order10B, Command>();
            cfg.RegisterRequestPipelineBehavior<Order10A, Command>();
            cfg.ConfigureBehaviorOrdering(typeof(Order10A), before: typeof(Order10B));
            cfg.ConfigureBehaviorOrdering(typeof(Order10C), after: typeof(Order10B));
        });

        // Act (When)
        var report = provider.ValidateSynapse();

        // Assert (Then)
        Assert.Empty(report.Issues);
    }

    [Fact]
    public void ValidateSynapse_WithThreeTiedBehaviorsAndOneConstraint_StillReportsSyn004()
    {
        // Arrange (Given) — A before B says nothing about C, so C's place is registration order.
        using var provider = BuildSync(cfg =>
        {
            cfg.RegisterRequestHandler<CommandHandler, Command>();
            cfg.RegisterRequestPipelineBehavior<Order10C, Command>();
            cfg.RegisterRequestPipelineBehavior<Order10B, Command>();
            cfg.RegisterRequestPipelineBehavior<Order10A, Command>();
            cfg.ConfigureBehaviorOrdering(typeof(Order10A), before: typeof(Order10B));
        });

        // Act (When)
        var report = provider.ValidateSynapse();

        // Assert (Then)
        var issue = Assert.Single(report.Warnings);
        Assert.Equal("SYN004", issue.Code);
    }

    // ── Registration API ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(typeof(Order10A), null, null, null)]
    [InlineData(typeof(Order10A), (PipelineStage)42, null, null)]
    [InlineData(typeof(Order10A), null, typeof(Order10A), null)]
    [InlineData(typeof(AuditBehavior<Query, int>), null, null, typeof(AuditBehavior<,>))]
    public void ConfigureBehaviorOrdering_WithAnInvalidDeclaration_Throws(Type behavior, PipelineStage? stage,
        Type? before, Type? after)
    {
        // Arrange (Given)
        var services = new ServiceCollection();

        // Act (When) / Assert (Then)
        Assert.Throws<ArgumentException>(() =>
            services.AddSynapse(cfg => cfg.ConfigureBehaviorOrdering(behavior, stage, before, after)));
    }

    [Fact]
    public void ConfigureBehaviorOrdering_WithANullBehavior_Throws()
    {
        // Arrange (Given)
        var services = new ServiceCollection();

        // Act (When) / Assert (Then)
        Assert.Throws<ArgumentNullException>(() =>
            services.AddSynapse(cfg => cfg.ConfigureBehaviorOrdering(null!, PipelineStage.Handler)));
    }

    // ── Describer ────────────────────────────────────────────────────────────

    [Fact]
    public void Describe_WithStagedBehaviors_ReportsTheResolvedOrderAndStages()
    {
        // Arrange (Given)
        using var provider = BuildSync(cfg =>
        {
            cfg.RegisterRequestHandler<CommandHandler, Command>();
            cfg.RegisterRequestPipelineBehavior<StagedValidation, Command>();
            cfg.RegisterRequestPipelineBehavior<Order10A, Command>();
            cfg.RegisterRequestPipelineBehavior<StagedAuthorization, Command>();
            cfg.ConfigureBehaviorOrdering(typeof(StagedValidation), PipelineStage.Validation);
            cfg.ConfigureBehaviorOrdering(typeof(StagedAuthorization), PipelineStage.Authorization);
        });

        // Act (When)
        var description = provider.GetRequiredService<IPipelineDescriber>().Describe<Command>();

        // Assert (Then)
        Assert.NotNull(description);
        Assert.Equal(
            [
                new BehaviorDescription(typeof(Order10A), 10, PipelineStage.Observability),
                new BehaviorDescription(typeof(StagedAuthorization), 0, PipelineStage.Authorization),
                new BehaviorDescription(typeof(StagedValidation), IOrderedPipelineBehavior.Last,
                    PipelineStage.Validation)
            ],
            description.Behaviors);
        Assert.Contains("Authorization 0 StagedAuthorization", description.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void BehaviorDescription_WithNamedArguments_KeepsThePositionalRecordNames()
    {
        // Arrange (Given) / Act (When) — named arguments as callers wrote them against the positional record.
        var description = new BehaviorDescription(Type: typeof(Order10A), Order: 10);
        var (type, order) = description;

        // Assert (Then)
        Assert.Equal(typeof(Order10A), type);
        Assert.Equal(10u, order);
        Assert.Equal(PipelineStage.Observability, description.Stage);
    }

    // ── PipelineStages ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(0u, PipelineStage.Observability)]
    [InlineData(0x1FFF_FFFFu, PipelineStage.Observability)]
    [InlineData(0x2000_0000u, PipelineStage.Authentication)]
    [InlineData(0x4000_0000u, PipelineStage.Authorization)]
    [InlineData(0x7FFF_FFFFu, PipelineStage.Validation)]
    [InlineData(0x8000_0000u, PipelineStage.Transaction)]
    [InlineData(0xA000_0000u, PipelineStage.Handler)]
    [InlineData(uint.MaxValue, PipelineStage.Handler)]
    public void Of_WithAnOrder_ReturnsTheStageWhoseBandContainsIt(uint order, PipelineStage expected)
    {
        // Act (When)
        var stage = PipelineStages.Of(order);

        // Assert (Then)
        Assert.Equal(expected, stage);
    }

    [Fact]
    public void StartOf_ForEachStage_RoundTripsThroughOf()
    {
        // Act (When) / Assert (Then)
        foreach (var stage in Enum.GetValues<PipelineStage>())
        {
            Assert.Equal(stage, PipelineStages.Of(PipelineStages.StartOf(stage)));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => PipelineStages.StartOf((PipelineStage)6));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ServiceProvider Build(Log log, Action<ISynapseConfig> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddLogging();
        services.AddSynapse(configure);
        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildSync(Action<ISynapseConfig> configure)
    {
        return Build(new Log(), configure);
    }

    private static async Task Invoke(IServiceProvider provider, Command command)
    {
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IInvoker>()
            .InvokeAsync(command, TestContext.Current.CancellationToken);
    }

    public sealed class Log
    {
        public List<string> Steps { get; } = [];
    }

    public sealed record Command : IRequest;

    public sealed record Query : IRequest<int>;

    public sealed record Notice : IEvent;

    public sealed record Count : IStreamRequest<int>;

    public sealed class CommandHandler : IRequestHandler<Command>
    {
        public ValueTask<Result> HandleAsync(Command request, CancellationToken cancellationToken = default)
        {
            return new ValueTask<Result>(Result.Success());
        }
    }

    public sealed class QueryHandler : IRequestHandler<Query, int>
    {
        public ValueTask<Result<int>> HandleAsync(Query request, CancellationToken cancellationToken = default)
        {
            return new ValueTask<Result<int>>(Result.Success(1));
        }
    }

    public sealed class NoticeHandler : IEventHandler<Notice>
    {
        public ValueTask<Result> HandleAsync(Notice @event, CancellationToken cancellationToken = default)
        {
            return new ValueTask<Result>(Result.Success());
        }
    }

    public sealed class CountHandler : IStreamRequestHandler<Count, int>
    {
        public async IAsyncEnumerable<Result<int>> HandleAsync(Count request,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return Result.Success(1);
        }
    }

    public abstract class LoggingBehavior(Log log, string name) : IRequestPipelineBehavior<Command>
    {
        public ValueTask<Result> HandleAsync(Command request, RequestHandlerDelegate<Command> next,
            CancellationToken cancellationToken = default)
        {
            log.Steps.Add(name);
            return next(request, cancellationToken);
        }
    }

    public sealed class Unordered(Log log) : LoggingBehavior(log, nameof(Unordered));

    public sealed class OrderFirst(Log log) : LoggingBehavior(log, nameof(OrderFirst)), IOrderedPipelineBehavior
    {
        public uint Order => IOrderedPipelineBehavior.First;
    }

    public sealed class Order10A(Log log) : LoggingBehavior(log, nameof(Order10A)), IOrderedPipelineBehavior
    {
        public uint Order => 10;
    }

    public sealed class Order10B(Log log) : LoggingBehavior(log, nameof(Order10B)), IOrderedPipelineBehavior
    {
        public uint Order => 10;
    }

    public sealed class Order10C(Log log) : LoggingBehavior(log, nameof(Order10C)), IOrderedPipelineBehavior
    {
        public uint Order => 10;
    }

    public sealed class Order10QueryBehavior(Log log) : IRequestPipelineBehavior<Query, int>, IOrderedPipelineBehavior
    {
        public uint Order => 10;

        public ValueTask<Result<int>> HandleAsync(Query request, RequestHandlerDelegate<Query, int> next,
            CancellationToken cancellationToken = default)
        {
            log.Steps.Add("Order10");
            return next(request, cancellationToken);
        }
    }

    public sealed class OrderMiddle(Log log) : LoggingBehavior(log, nameof(OrderMiddle)), IOrderedPipelineBehavior
    {
        public uint Order => IOrderedPipelineBehavior.Middle;
    }

    public sealed class OrderInValidationBand(Log log)
        : LoggingBehavior(log, nameof(OrderInValidationBand)), IOrderedPipelineBehavior
    {
        public uint Order => 0x6000_0010;
    }

    public sealed class VariableOrder(uint order) : IOrderedPipelineBehavior
    {
        public uint Order => order;
    }

    public sealed class StagedObservability(Log log) : LoggingBehavior(log, nameof(StagedObservability));

    // Order First would make it outermost, but its declared stage is Authorization.
    public sealed class StagedAuthorization(Log log)
        : LoggingBehavior(log, nameof(StagedAuthorization)), IOrderedPipelineBehavior
    {
        public uint Order => IOrderedPipelineBehavior.First;
    }

    public sealed class StagedValidation(Log log) : LoggingBehavior(log, nameof(StagedValidation));

    public sealed class ActorBehavior<TRequest, TResponse>(Log log) : IRequestPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
        where TResponse : notnull
    {
        public ValueTask<Result<TResponse>> HandleAsync(TRequest request,
            RequestHandlerDelegate<TRequest, TResponse> next, CancellationToken cancellationToken = default)
        {
            log.Steps.Add("Actor");
            return next(request, cancellationToken);
        }
    }

    public sealed class AuditBehavior<TRequest, TResponse>(Log log)
        : IRequestPipelineBehavior<TRequest, TResponse>, IOrderedPipelineBehavior
        where TRequest : IRequest<TResponse>
        where TResponse : notnull
    {
        public uint Order => IOrderedPipelineBehavior.First;

        public ValueTask<Result<TResponse>> HandleAsync(TRequest request,
            RequestHandlerDelegate<TRequest, TResponse> next, CancellationToken cancellationToken = default)
        {
            log.Steps.Add("Audit");
            return next(request, cancellationToken);
        }
    }

    public sealed class OuterEventBehavior(Log log) : IEventPipelineBehavior<Notice>, IOrderedPipelineBehavior
    {
        public uint Order => IOrderedPipelineBehavior.Last;

        public ValueTask<Result> HandleAsync(Notice @event, EventHandlerDelegate<Notice> next,
            CancellationToken cancellationToken = default)
        {
            log.Steps.Add("OuterEvent");
            return next(@event, cancellationToken);
        }
    }

    public sealed class InnerEventBehavior(Log log) : IEventPipelineBehavior<Notice>, IOrderedPipelineBehavior
    {
        public uint Order => 0xA000_0000;

        public ValueTask<Result> HandleAsync(Notice @event, EventHandlerDelegate<Notice> next,
            CancellationToken cancellationToken = default)
        {
            log.Steps.Add("InnerEvent");
            return next(@event, cancellationToken);
        }
    }

    public sealed class OuterStreamBehavior(Log log) : IStreamRequestPipelineBehavior<Count, int>
    {
        public IAsyncEnumerable<Result<int>> HandleAsync(Count request, StreamRequestHandlerDelegate<int> next,
            CancellationToken cancellationToken = default)
        {
            log.Steps.Add("OuterStream");
            return next();
        }
    }

    public sealed class InnerStreamBehavior(Log log) : IStreamRequestPipelineBehavior<Count, int>
    {
        public IAsyncEnumerable<Result<int>> HandleAsync(Count request, StreamRequestHandlerDelegate<int> next,
            CancellationToken cancellationToken = default)
        {
            log.Steps.Add("InnerStream");
            return next();
        }
    }
}
