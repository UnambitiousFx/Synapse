using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Publish.Outbox;

namespace UnambitiousFx.Synapse.Tests;

[TestSubject(typeof(SynapseConfig))]
public sealed class SynapseConfigEventOutboxStorageLifetimeTests
{
    [Fact]
    public void AddSynapse_WithNoStorageConfigured_RegistersInMemoryStorageAsSingleton()
    {
        // Arrange (Given) — the default must stay exactly as it is today: no one calls
        // SetEventOutboxStorage, so InMemoryEventOutboxStorage keeps its Singleton lifetime.
        var services = new ServiceCollection();

        // Act (When)
        services.AddSynapse(_ => { });

        // Assert (Then)
        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IEventOutboxStorage));
        Assert.Equal(typeof(InMemoryEventOutboxStorage), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void SetEventOutboxStorage_WithNoLifetimeArgument_RegistersScoped()
    {
        // Arrange (Given)
        var services = new ServiceCollection();

        // Act (When)
        services.AddSynapse(cfg => cfg.SetEventOutboxStorage<RecordingOutboxStorage>());

        // Assert (Then)
        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IEventOutboxStorage));
        Assert.Equal(typeof(RecordingOutboxStorage), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void SetEventOutboxStorage_WithExplicitSingleton_HonorsIt()
    {
        // Arrange (Given)
        var services = new ServiceCollection();

        // Act (When)
        services.AddSynapse(cfg =>
            cfg.SetEventOutboxStorage<RecordingOutboxStorage>(ServiceLifetime.Singleton));

        // Assert (Then)
        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IEventOutboxStorage));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void SetEventOutboxStorage_ScopedCustomStorage_ResolvesUnderValidatedScopes()
    {
        // Arrange (Given) — a scoped custom storage resolving cleanly with ValidateScopes = true is
        // the actual bug this fix closes: today it throws InvalidOperationException here.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSynapse(cfg => cfg.SetEventOutboxStorage<RecordingOutboxStorage>());
        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        // Act (When)
        using var scope = provider.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IEventOutboxStorage>();

        // Assert (Then)
        Assert.IsType<RecordingOutboxStorage>(storage);
    }

    [Fact]
    public void ScopedCustomStorage_SynapseMetricsResolvedFromRootProvider_DoesNotThrow()
    {
        // Arrange (Given) — ISynapseMetrics is registered Singleton and its outbox gauges read
        // IEventOutboxStorage. With a Scoped custom storage, resolving ISynapseMetrics itself from
        // the ROOT provider (as ASP.NET Core does at host build/validation time) must not throw, and
        // it must not capture the Scoped storage past that resolution — SynapseMetrics only reaches
        // into it lazily per gauge observation via a short-lived scope.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSynapse(cfg => cfg.SetEventOutboxStorage<RecordingOutboxStorage>());
        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        // Act (When)
        var metrics = provider.GetRequiredService<UnambitiousFx.Synapse.Observability.ISynapseMetrics>();

        // Assert (Then)
        Assert.NotNull(metrics);
    }

    private sealed class RecordingOutboxStorage : IEventOutboxStorage
    {
        public ValueTask<UnambitiousFx.Functional.Result> AddAsync<TEvent>(TEvent @event,
            IReadOnlyDictionary<string, string> headers,
            CancellationToken cancellationToken = default)
            where TEvent : class, IEvent
            => new(UnambitiousFx.Functional.Result.Success());

        public ValueTask<IReadOnlyList<OutboxEntry>> GetPendingEventsAsync(
            CancellationToken cancellationToken = default)
            => new(Array.Empty<OutboxEntry>() as IReadOnlyList<OutboxEntry>);

        public ValueTask<UnambitiousFx.Functional.Result> MarkAsProcessedAsync(Guid id,
            CancellationToken cancellationToken = default)
            => new(UnambitiousFx.Functional.Result.Success());

        public ValueTask<UnambitiousFx.Functional.Result> ClearAsync(
            CancellationToken cancellationToken = default)
            => new(UnambitiousFx.Functional.Result.Success());

        public ValueTask<UnambitiousFx.Functional.Result> MarkAsFailedAsync(Guid id,
            string reason,
            bool deadLetter,
            DateTimeOffset? nextAttemptAt = null,
            CancellationToken cancellationToken = default)
            => new(UnambitiousFx.Functional.Result.Success());

        public ValueTask<IReadOnlyList<OutboxEntry>> GetDeadLetterEventsAsync(
            CancellationToken cancellationToken = default)
            => new(Array.Empty<OutboxEntry>() as IReadOnlyList<OutboxEntry>);

        public ValueTask<int?> GetAttemptCountAsync(Guid id,
            CancellationToken cancellationToken = default)
            => new((int?)null);

        public ValueTask<int> GetPendingCountAsync(CancellationToken cancellationToken = default)
            => new(0);

        public ValueTask<int> GetRetryingCountAsync(CancellationToken cancellationToken = default)
            => new(0);

        public ValueTask<int> GetDeadLetterCountAsync(CancellationToken cancellationToken = default)
            => new(0);

        public ValueTask<TimeSpan?> GetOldestPendingAgeAsync(
            CancellationToken cancellationToken = default)
            => new((TimeSpan?)null);
    }
}
