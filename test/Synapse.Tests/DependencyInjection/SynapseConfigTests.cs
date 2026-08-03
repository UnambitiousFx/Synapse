using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Contexts;
using UnambitiousFx.Synapse.Pipelines;
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

    // ── AddRegisterGroup — IEventDispatcherRegistration auto-detection ──────

    [Fact]
    public void AddRegisterGroup_WhenGroupImplementsIEventDispatcherRegistration_RegistersDispatcherDelegate()
    {
        // Arrange (Given) — a register group that also implements IEventDispatcherRegistration
        var services = new ServiceCollection()
            .AddSynapse(cfg =>
            {
                cfg.AddRegisterGroup(new TestRegisterGroupWithDispatchers());
            })
            .AddLogging()
            .BuildServiceProvider();

        // Act (When)
        var options = services.GetRequiredService<IOptions<EventDispatcherOptions>>().Value;

        // Assert (Then) — dispatch delegate for EventExample registered automatically
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

    // ── AddValidator wires the validation behavior (issue 004) ───────────────

    [Fact]
    public void AddValidator_WithResponse_RegistersValidationBehavior()
    {
        // Arrange (Given)
        var services = new ServiceCollection();
        services.AddSynapse(cfg =>
        {
            cfg.AddValidator<TestWithResponseRequestValidator, RequestWithResponseExample, int>();
        });

        // Act (When)
        var descriptor = services.FirstOrDefault(x =>
            x.ServiceType == typeof(IRequestPipelineBehavior<RequestWithResponseExample, int>) &&
            x.ImplementationType == typeof(RequestValidationBehavior<RequestWithResponseExample, int>));

        // Assert (Then)
        Assert.NotNull(descriptor);
    }

    [Fact]
    public void AddValidator_NoResponse_RegistersValidationBehavior()
    {
        // Arrange (Given)
        var services = new ServiceCollection();
        services.AddSynapse(cfg =>
        {
            cfg.AddValidator<TestNoResponseRequestValidator, RequestExample>();
        });

        // Act (When)
        var descriptor = services.FirstOrDefault(x =>
            x.ServiceType == typeof(IRequestPipelineBehavior<RequestExample>) &&
            x.ImplementationType == typeof(RequestValidationBehavior<RequestExample>));

        // Assert (Then)
        Assert.NotNull(descriptor);
    }

    [Fact]
    public void AddValidator_MultipleValidatorsForSameRequest_RegistersBehaviorOnce()
    {
        // Arrange (Given) — two distinct validators target the same request
        var services = new ServiceCollection();
        services.AddSynapse(cfg =>
        {
            cfg.AddValidator<TestWithResponseRequestValidator, RequestWithResponseExample, int>();
            cfg.AddValidator<SecondWithResponseRequestValidator, RequestWithResponseExample, int>();
        });

        // Act (When)
        var behaviorDescriptors = services.Where(x =>
            x.ServiceType == typeof(IRequestPipelineBehavior<RequestWithResponseExample, int>) &&
            x.ImplementationType == typeof(RequestValidationBehavior<RequestWithResponseExample, int>)).ToList();
        var validatorDescriptors = services.Where(x =>
            x.ServiceType == typeof(IRequestValidator<RequestWithResponseExample>)).ToList();

        // Assert (Then) — behavior deduplicated to one; both validators kept.
        Assert.Single(behaviorDescriptors);
        Assert.Equal(2, validatorDescriptors.Count);
    }

    [Fact]
    public async Task AddValidator_Alone_RejectsInvalidRequest()
    {
        // Arrange (Given) — a handler plus a failing validator, wired ONLY through AddValidator
        // (no manual RequestValidationBehavior registration).
        var services = new ServiceCollection()
            .AddSynapse(cfg =>
            {
                cfg.RegisterRequestHandler<RequestWithResponseExampleHandler, RequestWithResponseExample, int>();
                cfg.AddValidator<AlwaysFailingValidator, RequestWithResponseExample, int>();
            })
            .AddLogging()
            .BuildServiceProvider();

        var invoker = services.GetRequiredService<IInvoker>();

        // Act (When)
        var result = await invoker.InvokeAsync(new RequestWithResponseExample(), TestContext.Current.CancellationToken);

        // Assert (Then) — validation ran and rejected the request before the handler returned success.
        Assert.False(result.IsSuccess);
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

    // ── RegisterRequestHandler — dispatcher delegate invocation ──────────────

    [Fact]
    public async Task RegisterRequestHandler_WithResponse_DispatcherDelegateInvokesHandlerViaInvoker()
    {
        // Arrange (Given)
        var services = new ServiceCollection()
            .AddSynapse(cfg =>
            {
                cfg.RegisterRequestHandler<RequestWithResponseExampleHandler, RequestWithResponseExample, int>();
            })
            .AddLogging()
            .BuildServiceProvider();
        var invoker = services.GetRequiredService<IInvoker>();

        // Act (When)
        var result = await invoker.InvokeAsync(new RequestWithResponseExample(), TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task RegisterRequestHandler_VoidRequest_DispatcherDelegateInvokesHandlerViaInvoker()
    {
        // Arrange (Given)
        var services = new ServiceCollection()
            .AddSynapse(cfg =>
            {
                cfg.RegisterRequestHandler<RequestExampleHandler, RequestExample>();
            })
            .AddLogging()
            .BuildServiceProvider();
        var invoker = services.GetRequiredService<IInvoker>();

        // Act (When)
        var result = await invoker.InvokeAsync(new RequestExample(), TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsSuccess);
    }

    // ── RegisterEventHandler — delegate body + type mismatch ─────────────────

    [Fact]
    public async Task RegisterEventHandler_DispatcherDelegate_InvokesEventDispatcher()
    {
        // Arrange (Given)
        var services = new ServiceCollection()
            .AddSynapse(cfg =>
            {
                cfg.RegisterEventHandler<EventExampleHandler1, EventExample>();
            })
            .AddLogging()
            .BuildServiceProvider();
        var options = services.GetRequiredService<IOptions<EventDispatcherOptions>>().Value;
        var del = options.Dispatchers[typeof(EventExample)];
        var eventDispatcher = Substitute.For<IEventDispatcher>();
        eventDispatcher.DispatchAsync(Arg.Any<EventExample>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Success()));

        // Act (When)
        var result = await del(new EventExample("test"), eventDispatcher, TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        await eventDispatcher.Received(1).DispatchAsync(Arg.Any<EventExample>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterEventHandler_DispatcherDelegate_WithWrongEventType_Throws()
    {
        // Arrange (Given)
        var services = new ServiceCollection()
            .AddSynapse(cfg =>
            {
                cfg.RegisterEventHandler<EventExampleHandler1, EventExample>();
            })
            .AddLogging()
            .BuildServiceProvider();
        var options = services.GetRequiredService<IOptions<EventDispatcherOptions>>().Value;
        var del = options.Dispatchers[typeof(EventExample)];
        var eventDispatcher = Substitute.For<IEventDispatcher>();

        // Act & Assert (When & Then)
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => del(new WrongSynapseConfigEvent(), eventDispatcher, TestContext.Current.CancellationToken).AsTask());
    }

    // ── Apply — EventDispatcherOptions concat branch (pre-existing dispatchers) ──

    [Fact]
    public void Apply_WithPreExistingEventDispatchers_MergesBothDispatcherDicts()
    {
        // Arrange (Given) — pre-configure a dispatcher for BaseEventExample before AddSynapse
        var services = new ServiceCollection();
        services.Configure<EventDispatcherOptions>(opt =>
        {
            opt.Dispatchers = new Dictionary<Type, DispatchEventDelegate>
            {
                [typeof(BaseEventExample)] = (_, _, _) => ValueTask.FromResult(Result.Success())
            };
        });
        services.AddSynapse(cfg =>
        {
            cfg.RegisterEventHandler<EventExampleHandler1, EventExample>();
        });
        var provider = services.AddLogging().BuildServiceProvider();

        // Act (When) — when options are resolved both Configure callbacks run;
        // Apply() sees Count != 0 and concats _eventDispatchers (EventExample) with existing (BaseEventExample)
        var options = provider.GetRequiredService<IOptions<EventDispatcherOptions>>().Value;

        // Assert (Then)
        Assert.Contains(typeof(EventExample), options.Dispatchers.Keys);
        Assert.Contains(typeof(BaseEventExample), options.Dispatchers.Keys);
    }

    // ── Private fixtures ─────────────────────────────────────────────────────

    private sealed record WrongSynapseConfigEvent : IEvent;

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

    private sealed class SecondWithResponseRequestValidator : IRequestValidator<RequestWithResponseExample>
    {
        public ValueTask<Result> ValidateAsync(RequestWithResponseExample request,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(Result.Success());
    }

    private sealed class AlwaysFailingValidator : IRequestValidator<RequestWithResponseExample>
    {
        public ValueTask<Result> ValidateAsync(RequestWithResponseExample request,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(Result.Failure("invalid"));
    }

    private sealed class CustomEventOutboxStorage : IEventOutboxStorage
    {
        public ValueTask<IReadOnlyList<OutboxEntry>> GetPendingEventsAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<OutboxEntry>>([]);

        public ValueTask<Result> MarkAsProcessedAsync(Guid id, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Result.Success());

        public ValueTask<Result> ClearAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Result.Success());

        public ValueTask<Result> AddAsync<TEvent>(TEvent @event,
            IReadOnlyDictionary<string, string> headers,
            CancellationToken cancellationToken = default)
            where TEvent : class, IEvent
            => ValueTask.FromResult(Result.Success());

        public ValueTask<Result> MarkAsFailedAsync(Guid id,
            string reason,
            bool deadLetter,
            DateTimeOffset? nextAttemptAt = null,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Result.Success());

        public ValueTask<IReadOnlyList<OutboxEntry>> GetDeadLetterEventsAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<OutboxEntry>>([]);

        public ValueTask<int?> GetAttemptCountAsync(Guid id, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<int?>(null);

        public ValueTask<int> GetPendingCountAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(0);

        public ValueTask<int> GetRetryingCountAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(0);

        public ValueTask<int> GetDeadLetterCountAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(0);

        public ValueTask<TimeSpan?> GetOldestPendingAgeAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult<TimeSpan?>(null);
    }

    private sealed class TestRegisterGroupWithDispatchers : IRegisterGroup, IEventDispatcherRegistration
    {
        public void Register(IDependencyInjectionBuilder builder)
        {
            // No handlers to register in this test fixture.
        }

        public void RegisterDispatchers(Action<Type, DispatchEventDelegate> register)
        {
            register(typeof(EventExample), (@event, dispatcher, ct) =>
                dispatcher.DispatchAsync((EventExample)@event, ct));
        }
    }
}
