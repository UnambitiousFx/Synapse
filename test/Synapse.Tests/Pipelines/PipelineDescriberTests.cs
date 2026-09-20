using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Pipelines;
using UnambitiousFx.Synapse.Tests.Definitions;

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

        // Assert (Then)
        Assert.Equal(1, services.Count(descriptor => descriptor.ServiceType == typeof(IPipelineDescriber)));
    }

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

    private static ServiceProvider BuildEvents(Trace trace, Action<ISynapseConfig> configure)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton(trace);
        services.AddSynapse(configure);
        return services.BuildServiceProvider();
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

    [Fact]
    public async Task Describe_WithABehaviorWhoseConstructorThrows_SurfacesTheOriginalException()
    {
        // Arrange (Given)
        await using var provider = Build(new Trace(),
            cfg => cfg.RegisterRequestPipelineBehavior<ThrowingBehavior<PlainCommand>, PlainCommand>());
        var describer = provider.GetRequiredService<IPipelineDescriber>();

        // Act (When)
        var exception = Record.Exception(() => describer.Describe(typeof(PlainCommand)));

        // Assert (Then) — not wrapped in a TargetInvocationException
        Assert.IsType<InvalidOperationException>(exception);
    }

    private static void AssertSameDescription(PipelineDescription? expected, PipelineDescription? actual)
    {
        Assert.NotNull(expected);
        Assert.NotNull(actual);
        Assert.NotEmpty(actual.Behaviors);
        Assert.Equal(expected, actual);
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

    private sealed class ThrowingBehavior<TRequest> : IRequestPipelineBehavior<TRequest>
        where TRequest : IRequest
    {
        public ThrowingBehavior()
        {
            throw new InvalidOperationException("needs a real request");
        }

        public ValueTask<Result> HandleAsync(TRequest request,
            RequestHandlerDelegate<TRequest> next,
            CancellationToken cancellationToken = default)
        {
            return next(request, cancellationToken);
        }
    }

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
}
