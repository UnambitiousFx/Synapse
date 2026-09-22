using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Publish.Outbox;

namespace UnambitiousFx.Synapse.Tests.Publish.Outbox;

[TestSubject(typeof(InMemoryOutboxProductionCheck))]
public sealed class InMemoryOutboxProductionCheckTests : IDisposable
{
    private readonly List<ServiceProvider> _providers = [];

    public void Dispose()
    {
        foreach (var provider in _providers)
        {
            provider.Dispose();
        }
    }

    [Fact]
    public async Task StartAsync_WithInMemoryStorageInProduction_LogsAWarning()
    {
        // Arrange (Given)
        var logger = new CapturingLogger();
        var check = new InMemoryOutboxProductionCheck(ScopeFactoryFor(new InMemoryEventOutboxStorage()), logger,
            EnvironmentNamed(Environments.Production));

        // Act (When)
        await check.StartAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("in-memory", entry.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    public async Task StartAsync_WithInMemoryStorageOutsideProduction_LogsNothing(string environmentName)
    {
        // Arrange (Given)
        var logger = new CapturingLogger();
        var check = new InMemoryOutboxProductionCheck(ScopeFactoryFor(new InMemoryEventOutboxStorage()), logger,
            EnvironmentNamed(environmentName));

        // Act (When)
        await check.StartAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task StartAsync_WithAnotherStorageInProduction_LogsNothing()
    {
        // Arrange (Given)
        var logger = new CapturingLogger();
        var check = new InMemoryOutboxProductionCheck(ScopeFactoryFor(Substitute.For<IEventOutboxStorage>()),
            logger, EnvironmentNamed(Environments.Production));

        // Act (When)
        await check.StartAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task StartAsync_WithoutAHostEnvironment_LogsNothing()
    {
        // Arrange (Given)
        var logger = new CapturingLogger();
        var check = new InMemoryOutboxProductionCheck(ScopeFactoryFor(new InMemoryEventOutboxStorage()), logger);

        // Act (When)
        await check.StartAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void AddSynapse_RegistersTheCheckAsAHostedService()
    {
        // Arrange (Given)
        var services = new ServiceCollection().AddLogging();

        // Act (When)
        services.AddSynapse(_ => { });
        services.AddSynapse(_ => { });
        using var provider = services.BuildServiceProvider();

        // Assert (Then) — twice registered, once running
        Assert.Single(provider.GetServices<IHostedService>().OfType<InMemoryOutboxProductionCheck>());
    }

    private static IHostEnvironment EnvironmentNamed(string name)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(name);
        return environment;
    }

    private IServiceScopeFactory ScopeFactoryFor(IEventOutboxStorage storage)
    {
        var services = new ServiceCollection();
        services.AddSingleton(storage);
        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        return provider.GetRequiredService<IServiceScopeFactory>();
    }

    private sealed class CapturingLogger : ILogger<InMemoryOutboxProductionCheck>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
