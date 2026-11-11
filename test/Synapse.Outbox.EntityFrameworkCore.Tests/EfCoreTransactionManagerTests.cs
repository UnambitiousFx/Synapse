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
    public async Task BeginAsync_ContextInAnotherTransaction_ThrowsAndOpensNoTransaction()
    {
        // Arrange (Given) — the outbox context already runs its own transaction, on a private database so the two
        // transactions do not contend for SQLite's write lock
        await using var database = await TransactionTestDatabase.CreateAsync(Ct);
        var services = new ServiceCollection();
        services.AddScoped(_ => new SqliteConnection(database.ConnectionString));
        services.AddDbContext<BusinessDbContext>((sp, o) => o.UseSqlite(sp.GetRequiredService<SqliteConnection>()));
        services.AddDbContext<OutboxDbContext>(o => o.UseSqlite("DataSource=:memory:"));
        services.AddEfCoreTransactions(t => t.Enlist<BusinessDbContext>().Enlist<OutboxDbContext>());
        services.AddScoped<EfCoreTransactionManager>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var (manager, business, outbox) = Resolve(scope);
        await using var outboxTransaction = await outbox.Database.BeginTransactionAsync(Ct);

        // Act (When)
        var exception = await Record.ExceptionAsync(async () => await manager.BeginAsync(Ct));

        // Assert (Then) — the transaction it opened on the first context is gone, the other one is untouched
        var invalid = Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("another transaction", invalid.Message);
        Assert.Null(business.Database.CurrentTransaction);
        Assert.Same(outboxTransaction, outbox.Database.CurrentTransaction);
    }

    [Fact]
    public async Task BeginAsync_WhenTheSavepointCannotBeCreated_ThrowsAndLeavesNoContextJoined()
    {
        // Arrange (Given) — an application transaction on the first context only, so the scope has to join the
        // outbox context to it before creating the savepoint
        await using var database = await TransactionTestDatabase.CreateAsync(Ct);
        var interceptor = new FailingSavepointInterceptor();
        var services = new ServiceCollection();
        database.AddContexts(services, interceptor);
        services.AddScoped<EfCoreTransactionManager>();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var (manager, business, outbox) = Resolve(scope);
        await using var applicationTransaction = await business.Database.BeginTransactionAsync(Ct);
        interceptor.FailNextCreate = true;

        // Act (When)
        var exception = await Record.ExceptionAsync(async () => await manager.BeginAsync(Ct));

        // Assert (Then) — the outbox context is detached again and the application transaction is still usable
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Null(outbox.Database.CurrentTransaction);
        Assert.Same(applicationTransaction, business.Database.CurrentTransaction);
        await using (var retry = await manager.BeginAsync(Ct))
        {
            await AddRecordAsync(business, "after-retry");
            await retry.CommitAsync(Ct);
        }

        await applicationTransaction.CommitAsync(Ct);
        Assert.Equal(["after-retry"], await database.CommittedRecordsAsync(Ct));
    }

    [Fact]
    public async Task BeginAsync_ConstructedWithoutAnyEnlistedContext_Throws()
    {
        // Arrange (Given) — built by hand, bypassing the check AddEfCoreTransactions makes
        await using var provider = new ServiceCollection().BuildServiceProvider();
        var manager = new EfCoreTransactionManager(provider, new EfCoreTransactionOptions());

        // Act (When)
        var exception = await Record.ExceptionAsync(async () => await manager.BeginAsync(Ct));

        // Assert (Then)
        var invalid = Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains(nameof(EfCoreTransactionOptions.Enlist), invalid.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommitAsync_CalledTwice_ThrowsAndKeepsTheFirstCommit(bool nested)
    {
        // Arrange (Given)
        await using var database = await TransactionTestDatabase.CreateAsync(Ct);
        await using var provider = BuildProvider(database);
        await using var scope = provider.CreateAsyncScope();
        var (manager, business, _) = Resolve(scope);
        await using var outer = await manager.BeginAsync(Ct);
        var target = nested ? await manager.BeginAsync(Ct) : outer;
        await AddRecordAsync(business, "committed-once");
        await target.CommitAsync(Ct);

        // Act (When)
        var exception = await Record.ExceptionAsync(async () => await target.CommitAsync(Ct));
        if (nested)
        {
            await target.DisposeAsync();
            await outer.CommitAsync(Ct);
        }

        // Assert (Then)
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(["committed-once"], await database.CommittedRecordsAsync(Ct));
    }

    [Fact]
    public async Task CommitAsync_WhenTheSavepointReleaseThrows_DisposeStillRollsBackToTheSavepoint()
    {
        // Arrange (Given)
        await using var database = await TransactionTestDatabase.CreateAsync(Ct);
        var interceptor = new FailingSavepointInterceptor();
        var services = new ServiceCollection();
        database.AddContexts(services, interceptor);
        services.AddScoped<EfCoreTransactionManager>();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var (manager, business, _) = Resolve(scope);
        Exception? releaseFailure;

        // Act (When)
        await using (var outer = await manager.BeginAsync(Ct))
        {
            await AddRecordAsync(business, "outer");
            await using (var inner = await manager.BeginAsync(Ct))
            {
                await AddRecordAsync(business, "inner");
                interceptor.FailNextRelease = true;
                releaseFailure = await Record.ExceptionAsync(async () => await inner.CommitAsync(Ct));
            }

            // The inner scope counts as closed again, so the outer one may commit.
            await outer.CommitAsync(Ct);
        }

        // Assert (Then)
        Assert.IsType<InvalidOperationException>(releaseFailure);
        Assert.Equal(["outer"], await database.CommittedRecordsAsync(Ct));
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
