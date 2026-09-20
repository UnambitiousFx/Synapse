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
        // Fan-out to many subscribers is the point of events
        // Arrange (Given)
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

        // Act (When)
        // Assert (Then)
        Assert.Throws<InvalidOperationException>(() => provider.ValidateSynapse());
    }

    [Fact]
    public void ValidateSynapse_WithNullProvider_Throws()
    {
        // Arrange (Given)
        // Act (When)
        // Assert (Then)
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
        private readonly IMissingDependency _dependency = dependency;

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
