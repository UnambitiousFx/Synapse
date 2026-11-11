using JetBrains.Annotations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Synapse.Outbox.EntityFrameworkCore.Tests.Support;

namespace UnambitiousFx.Synapse.Outbox.EntityFrameworkCore.Tests;

[TestSubject(typeof(EfCoreTransactionManager))]
public sealed class EfCoreTransactionManagerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task BeginAsync_ScopeCommitted_PersistsTheWritesOfEveryEnlistedContext()
    {
        // Arrange (Given)
        await using var database = await TransactionTestDatabase.CreateAsync(Ct);
        await using var provider = BuildProvider(database);
        await using var scope = provider.CreateAsyncScope();
        var (manager, business, outbox) = Resolve(scope);

        // Act (When)
        await using (var transaction = await manager.BeginAsync(Ct))
        {
            await AddRecordAsync(business, "envelope");
            await new EfCoreEventOutboxStorage<OutboxDbContext>(outbox).AddAsync(new OutboxTestEvent("closed"),
                new Dictionary<string, string>(), Ct);
            await transaction.CommitAsync(Ct);
        }

        // Assert (Then)
        Assert.Equal(["envelope"], await database.CommittedRecordsAsync(Ct));
        Assert.Single(await database.CommittedOutboxRowsAsync(Ct));
        Assert.Null(business.Database.CurrentTransaction);
        Assert.Null(outbox.Database.CurrentTransaction);
    }

    [Fact]
    public async Task BeginAsync_ScopeDisposedWithoutCommit_RollsBackEveryEnlistedContext()
    {
        // Arrange (Given)
        await using var database = await TransactionTestDatabase.CreateAsync(Ct);
        await using var provider = BuildProvider(database);
        await using var scope = provider.CreateAsyncScope();
        var (manager, business, outbox) = Resolve(scope);

        // Act (When)
        await using (await manager.BeginAsync(Ct))
        {
            await AddRecordAsync(business, "envelope");
            await new EfCoreEventOutboxStorage<OutboxDbContext>(outbox).AddAsync(new OutboxTestEvent("closed"),
                new Dictionary<string, string>(), Ct);
        }

        // Assert (Then)
        Assert.Empty(await database.CommittedRecordsAsync(Ct));
        Assert.Empty(await database.CommittedOutboxRowsAsync(Ct));
        Assert.Null(business.Database.CurrentTransaction);
        Assert.Null(outbox.Database.CurrentTransaction);
    }

    [Fact]
    public async Task BeginAsync_NestedScopeRolledBack_KeepsTheOuterScopeWrites()
    {
        // Arrange (Given)
        await using var database = await TransactionTestDatabase.CreateAsync(Ct);
        await using var provider = BuildProvider(database);
        await using var scope = provider.CreateAsyncScope();
        var (manager, business, outbox) = Resolve(scope);

        // Act (When)
        await using (var outer = await manager.BeginAsync(Ct))
        {
            await AddRecordAsync(business, "outer");
            await using (await manager.BeginAsync(Ct))
            {
                await AddRecordAsync(business, "inner");
                await new EfCoreEventOutboxStorage<OutboxDbContext>(outbox).AddAsync(new OutboxTestEvent("inner"),
                    new Dictionary<string, string>(), Ct);
            }

            await AddRecordAsync(business, "outer-after");
            await outer.CommitAsync(Ct);
        }

        // Assert (Then)
        Assert.Equal(["outer", "outer-after"], await database.CommittedRecordsAsync(Ct));
        Assert.Empty(await database.CommittedOutboxRowsAsync(Ct));
    }

    [Fact]
    public async Task BeginAsync_NestedScopeCommittedButOuterRolledBack_PersistsNothing()
    {
        // Arrange (Given)
        await using var database = await TransactionTestDatabase.CreateAsync(Ct);
        await using var provider = BuildProvider(database);
        await using var scope = provider.CreateAsyncScope();
        var (manager, business, _) = Resolve(scope);

        // Act (When)
        await using (await manager.BeginAsync(Ct))
        {
            await AddRecordAsync(business, "outer");
            await using (var inner = await manager.BeginAsync(Ct))
            {
                await AddRecordAsync(business, "inner");
                await inner.CommitAsync(Ct);
            }
        }

        // Assert (Then)
        Assert.Empty(await database.CommittedRecordsAsync(Ct));
    }

    [Fact]
    public async Task BeginAsync_ThreeLevelsDeep_RollsBackOnlyTheFailedLevel()
    {
        // Arrange (Given)
        await using var database = await TransactionTestDatabase.CreateAsync(Ct);
        await using var provider = BuildProvider(database);
        await using var scope = provider.CreateAsyncScope();
        var (manager, business, _) = Resolve(scope);

        // Act (When)
        await using (var level1 = await manager.BeginAsync(Ct))
        {
            await AddRecordAsync(business, "level1");
            await using (var level2 = await manager.BeginAsync(Ct))
            {
                await AddRecordAsync(business, "level2");
                await using (await manager.BeginAsync(Ct))
                {
                    await AddRecordAsync(business, "level3");
                }

                await level2.CommitAsync(Ct);
            }

            await level1.CommitAsync(Ct);
        }

        // Assert (Then)
        Assert.Equal(["level1", "level2"], await database.CommittedRecordsAsync(Ct));
    }

    [Fact]
    public async Task BeginAsync_WhenTheApplicationOpenedATransaction_OpensASavepointAndLeavesItOpen()
    {
        // Arrange (Given) — a transaction opened by hand on the first context only
        await using var database = await TransactionTestDatabase.CreateAsync(Ct);
        await using var provider = BuildProvider(database);
        await using var scope = provider.CreateAsyncScope();
        var (manager, business, outbox) = Resolve(scope);
        await using var applicationTransaction = await business.Database.BeginTransactionAsync(Ct);

        // Act (When)
        await using (await manager.BeginAsync(Ct))
        {
            await AddRecordAsync(business, "rolled-back");
            Assert.NotNull(outbox.Database.CurrentTransaction);
        }

        await using (var committed = await manager.BeginAsync(Ct))
        {
            await AddRecordAsync(business, "kept");
            await committed.CommitAsync(Ct);
        }

        var stillOpen = business.Database.CurrentTransaction;
        await applicationTransaction.CommitAsync(Ct);

        // Assert (Then)
        Assert.Same(applicationTransaction, stillOpen);
        Assert.Null(outbox.Database.CurrentTransaction);
        Assert.Equal(["kept"], await database.CommittedRecordsAsync(Ct));
    }

    [Fact]
    public async Task BeginAsync_ContextOnAnotherConnection_ThrowsAndOpensNoTransaction()
    {
        // Arrange (Given) — the outbox context gets its own connection instead of the scope's shared one
        await using var database = await TransactionTestDatabase.CreateAsync(Ct);
        var services = new ServiceCollection();
        services.AddScoped(_ => new SqliteConnection(database.ConnectionString));
        services.AddDbContext<BusinessDbContext>((sp, o) => o.UseSqlite(sp.GetRequiredService<SqliteConnection>()));
        services.AddDbContext<OutboxDbContext>(o => o.UseSqlite(database.ConnectionString));
        services.AddEfCoreTransactions(t => t.Enlist<BusinessDbContext>().Enlist<OutboxDbContext>());
        services.AddScoped<EfCoreTransactionManager>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var (manager, business, _) = Resolve(scope);

        // Act (When)
        var exception = await Record.ExceptionAsync(async () => await manager.BeginAsync(Ct));

        // Assert (Then)
        var invalid = Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("same DbConnection", invalid.Message);
        Assert.Null(business.Database.CurrentTransaction);
    }

    [Fact]
    public async Task CommitAsync_OuterScopeBeforeTheNestedOne_Throws()
    {
        // Arrange (Given)
        await using var database = await TransactionTestDatabase.CreateAsync(Ct);
        await using var provider = BuildProvider(database);
        await using var scope = provider.CreateAsyncScope();
        var (manager, business, _) = Resolve(scope);
        await using var outer = await manager.BeginAsync(Ct);
        await using var inner = await manager.BeginAsync(Ct);
        await AddRecordAsync(business, "never");

        // Act (When)
        var exception = await Record.ExceptionAsync(async () => await outer.CommitAsync(Ct));

        // Assert (Then)
        Assert.IsType<InvalidOperationException>(exception);
    }

    [Fact]
    public void AddEfCoreTransactions_WithoutAnyContext_Throws()
    {
        // Arrange (Given)
        var services = new ServiceCollection();

        // Act (When)
        var exception = Record.Exception(() => services.AddEfCoreTransactions(_ => { }));

        // Assert (Then)
        Assert.IsType<ArgumentException>(exception);
    }

    private static ServiceProvider BuildProvider(TransactionTestDatabase database)
    {
        var services = new ServiceCollection();
        database.AddContexts(services);
        services.AddScoped<EfCoreTransactionManager>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static (EfCoreTransactionManager Manager, BusinessDbContext Business, OutboxDbContext Outbox) Resolve(
        AsyncServiceScope scope)
    {
        return (scope.ServiceProvider.GetRequiredService<EfCoreTransactionManager>(),
            scope.ServiceProvider.GetRequiredService<BusinessDbContext>(),
            scope.ServiceProvider.GetRequiredService<OutboxDbContext>());
    }

    private static async Task AddRecordAsync(BusinessDbContext context, string name)
    {
        context.Records.Add(new BusinessRecord { Id = Guid.NewGuid(), Name = name });
        await context.SaveChangesAsync(Ct);
    }
}
