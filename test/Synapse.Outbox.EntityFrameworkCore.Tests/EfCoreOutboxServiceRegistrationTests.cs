using JetBrains.Annotations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Outbox.EntityFrameworkCore.Tests;

/// <summary>
///     Exercises the end-to-end container wiring the documentation leads with. Every other test in
///     this project constructs <see cref="EfCoreEventOutboxStorage{TContext}" /> by hand, so this is
///     the only place the documented three-line registration is actually proven to resolve — and the
///     only place the Scoped-by-default <c>SetEventOutboxStorage</c> fix is proven against the real EF
///     Core storage rather than a hand-rolled stub.
/// </summary>
/// <remarks>
///     This test project references <c>src/Synapse</c> for <c>AddSynapse</c>/<c>ISynapseConfig</c>.
///     The shipped <c>UnambitiousFx.Synapse.Outbox.EntityFrameworkCore</c> package deliberately does
///     not: it depends only on <c>Synapse.Abstractions</c>.
/// </remarks>
[TestSubject(typeof(ServiceCollectionExtensions))]
public sealed class EfCoreOutboxServiceRegistrationTests
{
    [Fact]
    public async Task DocumentedRegistration_ResolvesTheEfCoreStorageUnderValidatedScopes()
    {
        // Arrange (Given) — exactly the sample from docs/docs/outbox-entityframeworkcore.mdx, against
        // a real SQLite database. ValidateScopes + ValidateOnBuild is the configuration that used to
        // reject this wiring: IEventOutboxStorage was registered Singleton while the storage it names
        // depends on a Scoped DbContext.
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<OutboxDbContext>(o => o.UseSqlite(connection));
        services.AddEfCoreEventOutbox<OutboxDbContext>();
        services.AddSynapse(cfg => cfg.SetEventOutboxStorage<EfCoreEventOutboxStorage<OutboxDbContext>>());

        // Act (When)
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
        using var scope = provider.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IEventOutboxStorage>();

        // Assert (Then)
        Assert.IsType<EfCoreEventOutboxStorage<OutboxDbContext>>(storage);

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IEventOutboxStorage));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public async Task DocumentedRegistration_ResolvedStorageWritesThroughTheScopedDbContext()
    {
        // Arrange (Given) — resolving is not enough: the resolved storage must actually persist
        // through the scope's DbContext, i.e. the whole chain is wired, not merely constructible.
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<OutboxDbContext>(o => o.UseSqlite(connection));
        services.AddEfCoreEventOutbox<OutboxDbContext>();
        services.AddSynapse(cfg => cfg.SetEventOutboxStorage<EfCoreEventOutboxStorage<OutboxDbContext>>());

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        using (var setupScope = provider.CreateScope())
        {
            await setupScope.ServiceProvider.GetRequiredService<OutboxDbContext>()
                .Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        }

        // Act (When)
        using (var writeScope = provider.CreateScope())
        {
            var storage = writeScope.ServiceProvider.GetRequiredService<IEventOutboxStorage>();
            var addResult = await storage.AddAsync(new Support.OutboxTestEvent("wired"),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                TestContext.Current.CancellationToken);
            Assert.True(addResult.IsSuccess);
        }

        // Assert (Then) — visible from a different scope, so it really went to the database
        using var readScope = provider.CreateScope();
        var pending = await readScope.ServiceProvider.GetRequiredService<IEventOutboxStorage>()
            .GetPendingEventsAsync(TestContext.Current.CancellationToken);
        var entry = Assert.Single(pending);
        Assert.Equal("wired", Assert.IsType<Support.OutboxTestEvent>(entry.Event).Name);
    }
}
