using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using UnambitiousFx.Functional;
using UnambitiousFx.Functional.Failures;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Pipelines;

namespace UnambitiousFx.Synapse.Tests.Pipelines;

[TestSubject(typeof(TransactionBehavior<,>))]
public sealed class TransactionBehaviorTests
{
    // ── The behavior in isolation ───────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_WhenHandlerSucceeds_CommitsThenDisposesTheScope()
    {
        // Arrange (Given)
        var (manager, scope) = SubstituteManager();
        var behavior = new TransactionBehavior<TransactionalCommand, Result>(manager, new TransactionBehaviorOptions());
        var next = new RequestNext<TransactionalCommand, Result>((_, _) => new ValueTask<Result>(Result.Success()),
            Result.Failure);

        // Act (When)
        var result = await behavior.HandleAsync(new TransactionalCommand(), next, TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        Received.InOrder(() =>
        {
            manager.BeginAsync(Arg.Any<CancellationToken>());
            scope.CommitAsync(Arg.Any<CancellationToken>());
            scope.DisposeAsync();
        });
    }

    [Fact]
    public async Task HandleAsync_WhenHandlerSucceedsWithValue_CommitsAndReturnsTheValue()
    {
        // Arrange (Given)
        var (manager, scope) = SubstituteManager();
        var behavior =
            new TransactionBehavior<TransactionalQuery, Result<int>>(manager, new TransactionBehaviorOptions());
        var next = new RequestNext<TransactionalQuery, Result<int>>(
            (_, _) => new ValueTask<Result<int>>(Result.Success(42)), Result.Failure<int>);

        // Act (When)
        var result = await behavior.HandleAsync(new TransactionalQuery(), next, TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.TryGetValue(out var value));
        Assert.Equal(42, value);
        await scope.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        await scope.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task HandleAsync_WhenHandlerReturnsFailure_DisposesWithoutCommitting()
    {
        // Arrange (Given)
        var (manager, scope) = SubstituteManager();
        var behavior = new TransactionBehavior<TransactionalCommand, Result>(manager, new TransactionBehaviorOptions());
        var next = new RequestNext<TransactionalCommand, Result>(
            (_, _) => new ValueTask<Result>(Result.Failure(new Failure("envelope is closed"))), Result.Failure);

        // Act (When)
        var result = await behavior.HandleAsync(new TransactionalCommand(), next, TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.TryGetFailure(out var failure));
        Assert.Equal("envelope is closed", failure.Message);
        await scope.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        await scope.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task HandleAsync_WhenHandlerThrows_DisposesWithoutCommittingAndRethrows()
    {
        // Arrange (Given)
        var (manager, scope) = SubstituteManager();
        var behavior = new TransactionBehavior<TransactionalCommand, Result>(manager, new TransactionBehaviorOptions());
        var next = new RequestNext<TransactionalCommand, Result>(
            (_, _) => throw new InvalidOperationException("boom"), Result.Failure);

        // Act (When)
        var exception = await Record.ExceptionAsync(async () =>
            await behavior.HandleAsync(new TransactionalCommand(), next, TestContext.Current.CancellationToken));

        // Assert (Then)
        Assert.IsType<InvalidOperationException>(exception);
        await scope.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        await scope.Received(1).DisposeAsync();
    }

    [Fact]
    public void Order_WhenConfigured_ComesFromTheOptions()
    {
        // Arrange (Given)
        var (manager, _) = SubstituteManager();
        var options = new TransactionBehaviorOptions { Order = 7 };

        // Act (When)
        var behavior = new TransactionBehavior<TransactionalCommand, Result>(manager, options);

        // Assert (Then)
        Assert.Equal(7u, behavior.Order);
        Assert.Equal(IOrderedPipelineBehavior.Middle + 1, new TransactionBehaviorOptions().Order);
    }

    // ── Wired through AddSynapse ────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_WithAddTransactions_WrapsTransactionalRequestsOfBothShapes()
    {
        // Arrange (Given)
        var (manager, scope) = SubstituteManager();
        await using var provider = BuildProvider(manager, cfg => cfg.AddTransactions<StubTransactionManager>());
        await using var serviceScope = provider.CreateAsyncScope();
        var invoker = serviceScope.ServiceProvider.GetRequiredService<IInvoker>();

        // Act (When)
        var command = await invoker.InvokeAsync(new TransactionalCommand(), TestContext.Current.CancellationToken);
        var query = await invoker.InvokeAsync(new TransactionalQuery(), TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(command.IsSuccess);
        Assert.True(query.IsSuccess);
        await manager.Received(2).BeginAsync(Arg.Any<CancellationToken>());
        await scope.Received(2).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_WithAddTransactions_PassesNonTransactionalRequestsThrough()
    {
        // Arrange (Given)
        var (manager, _) = SubstituteManager();
        await using var provider = BuildProvider(manager, cfg => cfg.AddTransactions<StubTransactionManager>());
        await using var serviceScope = provider.CreateAsyncScope();
        var invoker = serviceScope.ServiceProvider.GetRequiredService<IInvoker>();

        // Act (When)
        var command = await invoker.InvokeAsync(new PlainCommand(), TestContext.Current.CancellationToken);
        var query = await invoker.InvokeAsync(new PlainQuery(), TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(command.IsSuccess);
        Assert.True(query.IsSuccess);
        await manager.DidNotReceive().BeginAsync(Arg.Any<CancellationToken>());
        var describer = provider.GetRequiredService<IPipelineDescriber>();
        Assert.DoesNotContain(describer.Describe<PlainCommand>()!.Behaviors,
            b => b.Type.IsGenericType &&
                 b.Type.GetGenericTypeDefinition() == typeof(TransactionBehavior<,>));
    }

    [Fact]
    public async Task InvokeAsync_WithAddTransactionsCalledTwice_OpensOneScopePerRequest()
    {
        // Arrange (Given)
        var (manager, _) = SubstituteManager();
        await using var provider = BuildProvider(manager, cfg =>
        {
            cfg.AddTransactions<StubTransactionManager>();
            cfg.AddTransactions<StubTransactionManager>(o => o.Order = 3);
        });
        await using var serviceScope = provider.CreateAsyncScope();
        var invoker = serviceScope.ServiceProvider.GetRequiredService<IInvoker>();

        // Act (When)
        await invoker.InvokeAsync(new TransactionalCommand(), TestContext.Current.CancellationToken);

        // Assert (Then) — one behavior, carrying the options of the last call
        await manager.Received(1).BeginAsync(Arg.Any<CancellationToken>());
        var behaviors = provider.GetRequiredService<IPipelineDescriber>().Describe<TransactionalCommand>()!.Behaviors;
        var transaction = Assert.Single(behaviors);
        Assert.Equal(typeof(TransactionBehavior<TransactionalCommand, Result>), transaction.Type);
        Assert.Equal(3u, transaction.Order);
    }

    [Fact]
    public async Task InvokeAsync_WithAddTransactionManagerOnly_DoesNotOpenAScope()
    {
        // Arrange (Given) — the AOT path: the behavior comes from the generator, not from this call
        var (manager, _) = SubstituteManager();
        await using var provider =
            BuildProvider(manager, cfg => cfg.AddTransactionManager<StubTransactionManager>());
        await using var serviceScope = provider.CreateAsyncScope();
        var invoker = serviceScope.ServiceProvider.GetRequiredService<IInvoker>();

        // Act (When)
        await invoker.InvokeAsync(new TransactionalCommand(), TestContext.Current.CancellationToken);

        // Assert (Then)
        await manager.DidNotReceive().BeginAsync(Arg.Any<CancellationToken>());
        Assert.IsType<StubTransactionManager>(serviceScope.ServiceProvider.GetRequiredService<ITransactionManager>());
    }

    [Fact]
    public async Task InvokeAsync_WithClosedRegistration_WrapsThatRequest()
    {
        // Arrange (Given) — what the generator emits for [assembly: SynapseGlobalBehavior(typeof(TransactionBehavior<,>))]
        var (manager, scope) = SubstituteManager();
        await using var provider = BuildProvider(manager, cfg =>
        {
            cfg.AddTransactionManager<StubTransactionManager>();
            cfg.RegisterResultPipelineBehavior<TransactionBehavior<TransactionalCommand, Result>, TransactionalCommand>();
        });
        await using var serviceScope = provider.CreateAsyncScope();
        var invoker = serviceScope.ServiceProvider.GetRequiredService<IInvoker>();

        // Act (When)
        await invoker.InvokeAsync(new TransactionalCommand(), TestContext.Current.CancellationToken);

        // Assert (Then)
        await scope.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    private static (ITransactionManager Manager, ITransactionScope Scope) SubstituteManager()
    {
        var scope = Substitute.For<ITransactionScope>();
        var manager = Substitute.For<ITransactionManager>();
        manager.BeginAsync(Arg.Any<CancellationToken>()).Returns(_ => new ValueTask<ITransactionScope>(scope));
        return (manager, scope);
    }

    private static ServiceProvider BuildProvider(ITransactionManager manager, Action<ISynapseConfig> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new ManagerHolder(manager));
        services.AddSynapse(cfg =>
        {
            cfg.RegisterRequestHandler<TransactionalCommandHandler, TransactionalCommand>();
            cfg.RegisterRequestHandler<TransactionalQueryHandler, TransactionalQuery, int>();
            cfg.RegisterRequestHandler<PlainCommandHandler, PlainCommand>();
            cfg.RegisterRequestHandler<PlainQueryHandler, PlainQuery, int>();
            configure(cfg);
        });
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    public sealed record TransactionalCommand : ICommand, ITransactional;

    public sealed record TransactionalQuery : ICommand<int>, ITransactional;

    public sealed record PlainCommand : ICommand;

    public sealed record PlainQuery : IQuery<int>;

    private sealed record ManagerHolder(ITransactionManager Manager);

    // A concrete type for AddTransactions<TManager>, forwarding to the substitute the test asserts on.
    private sealed class StubTransactionManager(ManagerHolder holder) : ITransactionManager
    {
        public ValueTask<ITransactionScope> BeginAsync(CancellationToken cancellationToken = default)
        {
            return holder.Manager.BeginAsync(cancellationToken);
        }
    }

    private sealed class TransactionalCommandHandler : IRequestHandler<TransactionalCommand>
    {
        public ValueTask<Result> HandleAsync(TransactionalCommand request,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<Result>(Result.Success());
        }
    }

    private sealed class TransactionalQueryHandler : IRequestHandler<TransactionalQuery, int>
    {
        public ValueTask<Result<int>> HandleAsync(TransactionalQuery request,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<Result<int>>(Result.Success(1));
        }
    }

    private sealed class PlainCommandHandler : IRequestHandler<PlainCommand>
    {
        public ValueTask<Result> HandleAsync(PlainCommand request, CancellationToken cancellationToken = default)
        {
            return new ValueTask<Result>(Result.Success());
        }
    }

    private sealed class PlainQueryHandler : IRequestHandler<PlainQuery, int>
    {
        public ValueTask<Result<int>> HandleAsync(PlainQuery request, CancellationToken cancellationToken = default)
        {
            return new ValueTask<Result<int>>(Result.Success(2));
        }
    }
}
