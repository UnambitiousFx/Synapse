using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Contexts;
using UnambitiousFx.Synapse.Publish;
using UnambitiousFx.Synapse.Publish.Orchestrators;
using UnambitiousFx.Synapse.Publish.Outbox;
using UnambitiousFx.Synapse.Tests.Definitions;

namespace UnambitiousFx.Synapse.Tests.DependencyInjection;

public sealed class SynapseConfigTests
{
    // ── SetDefaultPublishingMode ─────────────────────────────────────────────

    [Fact]
    public void SetDefaultPublishingMode_Outbox_ConfiguresPublisherOptions()
    {
        // Arrange (Given)
        var services = new ServiceCollection()
            .AddSynapse(cfg =>
            {
                cfg.SetDefaultPublishingMode(EmitMode.Outbox);
            })
            .AddLogging()
            .BuildServiceProvider();

        // Act (When)
        var options = services.GetRequiredService<IOptions<PublisherOptions>>().Value;

        // Assert (Then)
        Assert.Equal(EmitMode.Outbox, options.DefaultMode);
    }

    // ── SetEventOrchestrator ─────────────────────────────────────────────────

    [Fact]
    public void SetEventOrchestrator_Concurrent_ConfiguresOrchestratorType()
    {
        // Arrange (Given)
        var services = new ServiceCollection()
            .AddSynapse(cfg =>
            {
                cfg.SetEventOrchestrator<ConcurrentEventOrchestrator>();
            })
            .AddLogging()
            .BuildServiceProvider();

        // Act (When)
        var orchestrator = services.GetRequiredService<IEventOrchestrator>();

        // Assert (Then)
        Assert.IsType<ConcurrentEventOrchestrator>(orchestrator);
    }

    // ── SetEventOutboxStorage ────────────────────────────────────────────────

    [Fact]
    public void SetEventOutboxStorage_Custom_ConfiguresStorageType()
    {
        // Arrange (Given)
        var services = new ServiceCollection()
            .AddSynapse(cfg =>
            {
                cfg.SetEventOutboxStorage<CustomEventOutboxStorage>();
            })
            .AddLogging()
            .BuildServiceProvider();

        // Act (When)
        var storage = services.GetRequiredService<IEventOutboxStorage>();

        // Assert (Then)
        Assert.IsType<CustomEventOutboxStorage>(storage);
    }

    // ── ConfigureOutbox ──────────────────────────────────────────────────────

    [Fact]
    public void ConfigureOutbox_SetsMaxRetryAttempts()
    {
        // Arrange (Given)
        var services = new ServiceCollection()
            .AddSynapse(cfg =>
            {
                cfg.ConfigureOutbox(opt => opt.MaxRetryAttempts = 10);
            })
            .AddLogging()
            .BuildServiceProvider();

        // Act (When)
        var options = services.GetRequiredService<IOptions<OutboxOptions>>().Value;

        // Assert (Then)
        Assert.Equal(10, options.MaxRetryAttempts);
    }

    // ── UseSlimContextFactory ────────────────────────────────────────────────

    [Fact]
    public void UseSlimContextFactory_ConfiguresSlimContextFactory()
    {
        // Arrange (Given)
        var services = new ServiceCollection()
            .AddSynapse(cfg =>
            {
                cfg.UseSlimContextFactory();
            })
            .AddLogging()
            .BuildServiceProvider();

        // Act (When)
        var factory = services.GetRequiredService<IContextFactory>();

        // Assert (Then)
        Assert.IsType<SlimContextFactory>(factory);
    }

    // ── UseDefaultContextFactory ─────────────────────────────────────────────

    [Fact]
    public void UseDefaultContextFactory_ConfiguresDefaultContextFactory()
    {
        // Arrange (Given)
        var services = new ServiceCollection()
            .AddSynapse(cfg =>
            {
                cfg.UseDefaultContextFactory();
            })
            .AddLogging()
            .BuildServiceProvider();

        // Act (When)
        var factory = services.GetRequiredService<IContextFactory>();

        // Assert (Then)
        Assert.IsType<DefaultContextFactory>(factory);
    }

    // ── UseContextFactory<T> ─────────────────────────────────────────────────

    [Fact]
    public void UseContextFactory_CustomType_ConfiguresCustomContextFactory()
    {
        // Arrange (Given)
        var services = new ServiceCollection()
            .AddSynapse(cfg =>
            {
                cfg.UseContextFactory<SlimContextFactory>();
            })
            .AddLogging()
            .BuildServiceProvider();

        // Act (When)
        var factory = services.GetRequiredService<IContextFactory>();

        // Assert (Then)
        Assert.IsType<SlimContextFactory>(factory);
    }

    // ── UseEventDispatcherRegistration ───────────────────────────────────────

    [Fact]
    public void UseEventDispatcherRegistration_RegistersDispatcherDelegate()
    {
        // Arrange (Given)
        var services = new ServiceCollection()
            .AddSynapse(cfg =>
            {
                cfg.UseEventDispatcherRegistration<TestEventDispatcherRegistration>();
            })
            .AddLogging()
            .BuildServiceProvider();

        // Act (When)
        var options = services.GetRequiredService<IOptions<EventDispatcherOptions>>().Value;

        // Assert (Then)
        Assert.Contains(typeof(EventExample), options.Dispatchers.Keys);
    }

    // ── AddValidator (no-response) ───────────────────────────────────────────

    [Fact]
    public void AddValidator_NoResponse_RegistersValidator()
    {
        // Arrange (Given) — RequestExample implements IRequest (no-response marker)
        var services = new ServiceCollection()
            .AddSynapse(cfg =>
            {
                cfg.AddValidator<TestNoResponseRequestValidator, RequestExample>();
            })
            .AddLogging()
            .BuildServiceProvider();

        // Act (When)
        var validator = services.GetService<IRequestValidator<RequestExample>>();

        // Assert (Then)
        Assert.NotNull(validator);
        Assert.IsType<TestNoResponseRequestValidator>(validator);
    }

    // ── AddValidator (with response) ─────────────────────────────────────────

    [Fact]
    public void AddValidator_WithResponse_RegistersValidator()
    {
        // Arrange (Given) — RequestWithResponseExample implements IRequest<int>
        var services = new ServiceCollection()
            .AddSynapse(cfg =>
            {
                cfg.AddValidator<TestWithResponseRequestValidator, RequestWithResponseExample, int>();
            })
            .AddLogging()
            .BuildServiceProvider();

        // Act (When)
        var validator = services.GetService<IRequestValidator<RequestWithResponseExample>>();

        // Assert (Then)
        Assert.NotNull(validator);
        Assert.IsType<TestWithResponseRequestValidator>(validator);
    }

    // ── AddOpenGenericRequestPipelineBehavior ────────────────────────────────

    [Fact]
    public void AddOpenGenericRequestPipelineBehavior_RegistersOpenGenericDescriptor()
    {
        // Arrange (Given)
        var services = new ServiceCollection();
        services.AddSynapse(cfg =>
        {
            cfg.AddOpenGenericRequestPipelineBehavior(typeof(TestRequestPipelineBehavior<>));
        });

        // Act (When)
        var descriptor = services.FirstOrDefault(x =>
            x.ServiceType == typeof(IRequestPipelineBehavior<>) &&
            x.ImplementationType == typeof(TestRequestPipelineBehavior<>));

        // Assert (Then)
        Assert.NotNull(descriptor);
    }

    // ── AddOpenGenericRequestWithResponsePipelineBehavior ────────────────────

    [Fact]
    public void AddOpenGenericRequestWithResponsePipelineBehavior_RegistersOpenGenericDescriptor()
    {
        // Arrange (Given)
        var services = new ServiceCollection();
        services.AddSynapse(cfg =>
        {
            cfg.AddOpenGenericRequestWithResponsePipelineBehavior(
                typeof(TestRequestPipelineBehavior<,>));
        });

        // Act (When)
        var descriptor = services.FirstOrDefault(x =>
            x.ServiceType == typeof(IRequestPipelineBehavior<,>) &&
            x.ImplementationType == typeof(TestRequestPipelineBehavior<,>));

        // Assert (Then)
        Assert.NotNull(descriptor);
    }

    // ── AddOpenGenericEventPipelineBehavior ──────────────────────────────────

    [Fact]
    public void AddOpenGenericEventPipelineBehavior_RegistersOpenGenericDescriptor()
    {
        // Arrange (Given)
        var services = new ServiceCollection();
        services.AddSynapse(cfg =>
        {
            cfg.AddOpenGenericEventPipelineBehavior(typeof(TestEventPipelineBehavior<>));
        });

        // Act (When)
        var descriptor = services.FirstOrDefault(x =>
            x.ServiceType == typeof(IEventPipelineBehavior<>) &&
            x.ImplementationType == typeof(TestEventPipelineBehavior<>));

        // Assert (Then)
        Assert.NotNull(descriptor);
    }

    // ── AddOpenGenericStreamRequestPipelineBehavior ──────────────────────────

    [Fact]
    public void AddOpenGenericStreamRequestPipelineBehavior_RegistersOpenGenericDescriptor()
    {
        // Arrange (Given)
        var services = new ServiceCollection();
        services.AddSynapse(cfg =>
        {
            cfg.AddOpenGenericStreamRequestPipelineBehavior(typeof(TestStreamRequestPipelineBehavior<,>));
        });

        // Act (When)
        var descriptor = services.FirstOrDefault(x =>
            x.ServiceType == typeof(IStreamRequestPipelineBehavior<,>) &&
            x.ImplementationType == typeof(TestStreamRequestPipelineBehavior<,>));

        // Assert (Then)
        Assert.NotNull(descriptor);
    }

    // ── RegisterEventPipelineBehavior (typed) ────────────────────────────────

    [Fact]
    public void RegisterEventPipelineBehavior_RegistersBehaviorForEvent()
    {
        // Arrange (Given)
        var services = new ServiceCollection()
            .AddSynapse(cfg =>
            {
                cfg.RegisterEventHandler<EventExampleHandler1, EventExample>();
                cfg.RegisterEventPipelineBehavior<TestEventPipelineBehavior<EventExample>, EventExample>();
            })
            .AddLogging()
            .BuildServiceProvider();

        // Act (When)
        var behavior = services.GetService<IEventPipelineBehavior<EventExample>>();

        // Assert (Then)
        Assert.NotNull(behavior);
        Assert.IsType<TestEventPipelineBehavior<EventExample>>(behavior);
    }

    // ── RegisterStreamRequestPipelineBehavior (typed) ────────────────────────

    [Fact]
    public void RegisterStreamRequestPipelineBehavior_RegistersBehaviorForStreamRequest()
    {
        // Arrange (Given)
        var services = new ServiceCollection()
            .AddSynapse(cfg =>
            {
                cfg.RegisterStreamRequestPipelineBehavior<
                    TestStreamRequestPipelineBehavior<TestStreamRequest, int>,
                    TestStreamRequest,
                    int>();
            })
            .AddLogging()
            .BuildServiceProvider();

        // Act (When)
        var behavior = services.GetService<IStreamRequestPipelineBehavior<TestStreamRequest, int>>();

        // Assert (Then)
        Assert.NotNull(behavior);
    }

    // ── Private fixtures ─────────────────────────────────────────────────────

    private sealed record TestStreamRequest : IStreamRequest<int>;

    private sealed class TestStreamRequestPipelineBehavior<TRequest, TItem>
        : IStreamRequestPipelineBehavior<TRequest, TItem>
        where TRequest : IStreamRequest<TItem>
        where TItem : notnull
    {
        public async IAsyncEnumerable<Result<TItem>> HandleAsync(TRequest request,
            StreamRequestHandlerDelegate<TItem> next,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var item in next()) yield return item;
        }
    }

    private sealed class TestNoResponseRequestValidator : IRequestValidator<RequestExample>
    {
        public ValueTask<Result> ValidateAsync(RequestExample request,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(Result.Success());
    }

    private sealed class TestWithResponseRequestValidator : IRequestValidator<RequestWithResponseExample>
    {
        public ValueTask<Result> ValidateAsync(RequestWithResponseExample request,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(Result.Success());
    }

    private sealed class CustomEventOutboxStorage : IEventOutboxStorage
    {
        public ValueTask<IEnumerable<IEvent>> GetPendingEventsAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Enumerable.Empty<IEvent>());

        public ValueTask<Result> MarkAsProcessedAsync(IEvent @event, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Result.Success());

        public ValueTask<Result> ClearAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Result.Success());

        public ValueTask<Result> AddAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
            where TEvent : class, IEvent
            => ValueTask.FromResult(Result.Success());

        public ValueTask<Result> MarkAsFailedAsync(IEvent @event,
            string reason,
            bool deadLetter,
            DateTimeOffset? nextAttemptAt = null,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Result.Success());

        public ValueTask<IEnumerable<IEvent>> GetDeadLetterEventsAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Enumerable.Empty<IEvent>());

        public ValueTask<int?> GetAttemptCountAsync(IEvent @event, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<int?>(null);

        public ValueTask<int> GetPendingCountAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(0);

        public ValueTask<int> GetFailedCountAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(0);

        public ValueTask<TimeSpan?> GetOldestPendingAgeAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult<TimeSpan?>(null);
    }

    private sealed class TestEventDispatcherRegistration : IEventDispatcherRegistration
    {
        public void RegisterDispatchers(Action<Type, DispatchEventDelegate> register)
        {
            register(typeof(EventExample), (@event, dispatcher, ct) =>
                dispatcher.DispatchAsync((EventExample)@event, ct));
        }
    }
}
