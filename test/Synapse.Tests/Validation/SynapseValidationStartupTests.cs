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
