using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Tests.Definitions;

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
        Assert.DoesNotContain(sink.Warnings, message => message.Contains("SYN", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartAsync_WithoutValidateOnStart_DoesNotValidate()
    {
        // Arrange (Given)
        // Same misconfiguration as the first test, but never opted in
        await using var provider = Build(new LogSink(),
            cfg => cfg.RegisterEventPipelineBehavior<NoopEventBehavior, PingEvent>());

        // Act (When)
        var exception = await Record.ExceptionAsync(() => StartHostedServices(provider));

        // Assert (Then)
        Assert.Null(exception);
    }

    [Fact]
    public async Task StartAsync_ResolvingPipelines_RunsOnADedicatedNamedThread()
    {
        // Arrange (Given)
        var probe = new ThreadProbe();
        await using var provider = Build(new LogSink(), cfg =>
        {
            cfg.RegisterEventHandler<ThreadCapturingHandler, PingEvent>();
            cfg.ValidateOnStart();
        }, services => services.AddSingleton(probe));

        // Act (When)
        await StartHostedServices(provider);

        // Assert (Then)
        var thread = Assert.IsType<Thread>(probe.Thread);
        Assert.NotSame(Thread.CurrentThread, thread);
        Assert.Equal("Synapse start-up validation", thread.Name);
    }

    [Fact]
    public async Task StartAsync_CancelledBeforeValidationCompletes_ThrowsOperationCanceledException()
    {
        // Arrange (Given)
        var gate = new ManualResetEventSlim(); // not disposed: the validation thread may still be leaving Wait()
        using var cancellation = new CancellationTokenSource();
        await using var provider = Build(new LogSink(), cfg =>
        {
            cfg.RegisterEventHandler<GatedHandler, PingEvent>();
            cfg.ValidateOnStart();
        }, services => services.AddSingleton(gate));
        var hosted = provider.GetServices<IHostedService>().Single(service =>
            service.GetType().Name == "SynapseValidationStartup");

        // Act (When)
        var start = hosted.StartAsync(cancellation.Token);
        await cancellation.CancelAsync();
        var exception = await Record.ExceptionAsync(() => start);
        gate.Set();

        // Assert (Then)
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
    }

    [Fact]
    public async Task StartAsync_ValidationThrows_RethrowsTheSameException()
    {
        // Arrange (Given) — without the registry, ValidateSynapse throws on the validation thread
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(new LogSink()));
        services.AddSynapse(cfg => cfg.ValidateOnStart());
        services.RemoveAll<UnambitiousFx.Synapse.Validation.SynapseRegistry>();
        await using var provider = services.BuildServiceProvider();

        // Act (When)
        var exception = await Record.ExceptionAsync(() => StartHostedServices(provider));

        // Assert (Then)
        var invalid = Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("Synapse is not registered", invalid.Message);
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

    [Fact]
    public async Task StartAsync_WithAnAsyncOnlyDisposableScopedDependency_Starts()
    {
        // Arrange (Given)
        var counter = new AsyncDisposeCounter();
        await using var provider = Build(new LogSink(), cfg =>
        {
            cfg.RegisterRequestHandler<AsyncOnlyHandler, AsyncOnlyRequest>();
            cfg.ValidateOnStart();
        }, services =>
        {
            services.AddSingleton(counter);
            services.AddScoped<AsyncOnlyDisposable>();
        });

        // Act (When)
        var exception = await Record.ExceptionAsync(() => StartHostedServices(provider));

        // Assert (Then)
        Assert.Null(exception);
        Assert.True(counter.Count >= 1);
    }

    private sealed record AsyncOnlyRequest : IRequest;

    private sealed class AsyncOnlyHandler(AsyncOnlyDisposable dependency) : IRequestHandler<AsyncOnlyRequest>
    {
        public ValueTask<Result> HandleAsync(AsyncOnlyRequest request, CancellationToken cancellationToken = default)
        {
            _ = dependency;
            return new ValueTask<Result>(Result.Success());
        }
    }

    private static async Task StartHostedServices(IServiceProvider provider)
    {
        foreach (var hosted in provider.GetServices<IHostedService>())
        {
            await hosted.StartAsync(TestContext.Current.CancellationToken);
        }
    }

    private static ServiceProvider Build(LogSink sink, Action<ISynapseConfig> configure,
        Action<IServiceCollection>? arrange = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(sink));
        arrange?.Invoke(services);
        services.AddSynapse(configure);
        return services.BuildServiceProvider();
    }

    private sealed record PingEvent : IEvent;

    private sealed class PingHandler : IEventHandler<PingEvent>
    {
        public ValueTask<Result> HandleAsync(PingEvent @event, CancellationToken cancellationToken = default) =>
            new(Result.Success());
    }

    private sealed class ThreadProbe
    {
        public Thread? Thread { get; set; }
    }

    private sealed class ThreadCapturingHandler : IEventHandler<PingEvent>
    {
        public ThreadCapturingHandler(ThreadProbe probe)
        {
            probe.Thread = Thread.CurrentThread;
        }

        public ValueTask<Result> HandleAsync(PingEvent @event, CancellationToken cancellationToken = default) =>
            new(Result.Success());
    }

    // Holds validation until the test has cancelled the start.
    private sealed class GatedHandler : IEventHandler<PingEvent>
    {
        public GatedHandler(ManualResetEventSlim gate)
        {
            gate.Wait();
        }

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
