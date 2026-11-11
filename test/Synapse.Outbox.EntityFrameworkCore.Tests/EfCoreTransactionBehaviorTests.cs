using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UnambitiousFx.Functional;
using UnambitiousFx.Functional.Failures;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Outbox.EntityFrameworkCore.Tests.Support;
using UnambitiousFx.Synapse.Pipelines;

namespace UnambitiousFx.Synapse.Outbox.EntityFrameworkCore.Tests;

/// <summary>
///     The documented wiring end to end: <c>AddTransactions&lt;EfCoreTransactionManager&gt;()</c> around handlers
///     that write a business context and the EF Core outbox, both on one shared connection.
/// </summary>
[TestSubject(typeof(TransactionBehavior<,>))]
public sealed class EfCoreTransactionBehaviorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task InvokeAsync_WhenHandlerSucceeds_CommitsTheRecordAndDispatchesItsEvent()
    {
        // Arrange (Given)
        await using var database = await TransactionTestDatabase.CreateAsync(Ct);
        var recorder = new EventRecorder();
        await using var provider = BuildProvider(database, recorder);

        // Act (When)
        var result = await InvokeAsync(provider, new CreateRecord("envelope", Fail: false));

        // Assert (Then)
        Assert.True(result.IsSuccess);
        Assert.Equal(["envelope"], await database.CommittedRecordsAsync(Ct));
        Assert.Equal(["envelope"], recorder.Dispatched);
        Assert.True(Assert.Single(await database.CommittedOutboxRowsAsync(Ct)).Processed);
    }

    [Fact]
    public async Task InvokeAsync_WhenHandlerReturnsFailure_RollsBackTheRecordAndItsEvent()
    {
        // Arrange (Given)
        await using var database = await TransactionTestDatabase.CreateAsync(Ct);
        var recorder = new EventRecorder();
        await using var provider = BuildProvider(database, recorder);

        // Act (When)
        var result = await InvokeAsync(provider, new CreateRecord("envelope", Fail: true));

        // Assert (Then)
        Assert.True(result.IsFailure);
        Assert.Empty(await database.CommittedRecordsAsync(Ct));
        Assert.Empty(await database.CommittedOutboxRowsAsync(Ct));
        Assert.Empty(recorder.Dispatched);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvokeAsync_WhenTheOutboxFlushFailsAfterTheCommit_StillSucceedsAndKeepsTheEventPending(
        bool cancelTheRequest)
    {
        // Arrange (Given) — the flush either throws or sees the request's token canceled while it runs
        await using var database = await TransactionTestDatabase.CreateAsync(Ct);
        var recorder = new EventRecorder();
        using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var fault = new FlushFault(cancelTheRequest ? requestCancellation : null);
        await using var provider = BuildProvider(database, recorder, services =>
        {
            services.AddSingleton(fault);
            services.Replace(ServiceDescriptor.Scoped<IOutboxCommit, FaultyOutboxCommit>());
        });
        await using var scope = provider.CreateAsyncScope();
        var invoker = scope.ServiceProvider.GetRequiredService<IInvoker>();

        // Act (When)
        var result = await invoker.InvokeAsync(new CreateRecord("envelope", Fail: false), requestCancellation.Token);

        // Assert (Then)
        Assert.True(fault.Called);
        Assert.True(result.IsSuccess);
        Assert.Equal(["envelope"], await database.CommittedRecordsAsync(Ct));
        Assert.False(Assert.Single(await database.CommittedOutboxRowsAsync(Ct)).Processed);
        Assert.Empty(recorder.Dispatched);
    }

    [Fact]
    public async Task InvokeAsync_WhenNestedCommandFails_KeepsTheOuterCommandWork()
    {
        // Arrange (Given)
        await using var database = await TransactionTestDatabase.CreateAsync(Ct);
        var recorder = new EventRecorder();
        await using var provider = BuildProvider(database, recorder);

        // Act (When)
        var result = await InvokeAsync(provider, new CreateWithChild("parent", "child"));

        // Assert (Then) — the child rolled back to its savepoint; the parent and its event committed
        Assert.True(result.IsSuccess);
        Assert.Equal(["parent"], await database.CommittedRecordsAsync(Ct));
        Assert.Equal(["parent"], recorder.Dispatched);
    }

    [Fact]
    public async Task InvokeAsync_WithNonTransactionalCommand_SavesWithoutATransaction()
    {
        // Arrange (Given)
        await using var database = await TransactionTestDatabase.CreateAsync(Ct);
        var recorder = new EventRecorder();
        await using var provider = BuildProvider(database, recorder);
        await using var scope = provider.CreateAsyncScope();
        var invoker = scope.ServiceProvider.GetRequiredService<IInvoker>();

        // Act (When)
        var result = await invoker.InvokeAsync(new SaveWithoutTransaction("plain"), Ct);

        // Assert (Then)
        Assert.True(result.TryGetValue(out var hadTransaction));
        Assert.False(hadTransaction);
        Assert.Equal(["plain"], await database.CommittedRecordsAsync(Ct));
    }

    private static async Task<Result> InvokeAsync(ServiceProvider provider, IRequest request)
    {
        await using var scope = provider.CreateAsyncScope();
        var invoker = scope.ServiceProvider.GetRequiredService<IInvoker>();
        return request switch
        {
            CreateRecord create => await invoker.InvokeAsync(create, Ct),
            CreateWithChild parent => await invoker.InvokeAsync(parent, Ct),
            _ => throw new ArgumentOutOfRangeException(nameof(request))
        };
    }

    private static ServiceProvider BuildProvider(TransactionTestDatabase database,
        EventRecorder recorder,
        Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(recorder);
        database.AddContexts(services);
        services.AddEfCoreEventOutbox<OutboxDbContext>();
        services.AddSynapse(cfg =>
        {
            cfg.SetEventOutboxStorage<EfCoreEventOutboxStorage<OutboxDbContext>>();
            cfg.AddTransactions<EfCoreTransactionManager>();
            cfg.RegisterRequestHandler<CreateRecordHandler, CreateRecord>();
            cfg.RegisterRequestHandler<CreateWithChildHandler, CreateWithChild>();
            cfg.RegisterRequestHandler<SaveWithoutTransactionHandler, SaveWithoutTransaction, bool>();
            cfg.RegisterEventHandler<RecordCreatedHandler, RecordCreated>();
        });
        configure?.Invoke(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    public sealed record CreateRecord(string Name, bool Fail) : ICommand, ITransactional;

    public sealed record CreateWithChild(string Name, string ChildName) : ICommand, ITransactional;

    public sealed record SaveWithoutTransaction(string Name) : ICommand<bool>;

    public sealed record RecordCreated(string Name) : IEvent;

    public sealed class EventRecorder
    {
        private readonly List<string> _dispatched = [];

        public IReadOnlyList<string> Dispatched
        {
            get
            {
                lock (_dispatched)
                {
                    return _dispatched.ToList();
                }
            }
        }

        public void Record(string name)
        {
            lock (_dispatched)
            {
                _dispatched.Add(name);
            }
        }
    }

    /// <summary>What the faulty outbox commit does: cancel the request's token, or throw an infrastructure error.</summary>
    public sealed class FlushFault(CancellationTokenSource? cancelRequest)
    {
        public bool Called { get; private set; }

        public void Raise(CancellationToken cancellationToken)
        {
            Called = true;
            if (cancelRequest is null)
            {
                throw new InvalidOperationException("Simulated outbox infrastructure failure.");
            }

            cancelRequest.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private sealed class FaultyOutboxCommit(FlushFault fault) : IOutboxCommit
    {
        public ValueTask<Result> CommitAsync(CancellationToken cancellationToken = default)
        {
            fault.Raise(cancellationToken);
            return new ValueTask<Result>(Result.Success());
        }
    }

    private sealed class CreateRecordHandler(BusinessDbContext context, IEmitter emitter)
        : IRequestHandler<CreateRecord>
    {
        public async ValueTask<Result> HandleAsync(CreateRecord request, CancellationToken cancellationToken = default)
        {
            context.Records.Add(new BusinessRecord { Id = Guid.NewGuid(), Name = request.Name });
            await context.SaveChangesAsync(cancellationToken);
            await emitter.EmitAsync(new RecordCreated(request.Name), EmitMode.Outbox, cancellationToken);
            return request.Fail ? Result.Failure(new Failure("rejected after saving")) : Result.Success();
        }
    }

    private sealed class CreateWithChildHandler(BusinessDbContext context, IEmitter emitter, IInvoker invoker)
        : IRequestHandler<CreateWithChild>
    {
        public async ValueTask<Result> HandleAsync(CreateWithChild request,
            CancellationToken cancellationToken = default)
        {
            context.Records.Add(new BusinessRecord { Id = Guid.NewGuid(), Name = request.Name });
            await context.SaveChangesAsync(cancellationToken);
            await emitter.EmitAsync(new RecordCreated(request.Name), EmitMode.Outbox, cancellationToken);

            // The child's failure is tolerated here: only its own savepoint rolls back.
            var child = await invoker.InvokeAsync(new CreateRecord(request.ChildName, Fail: true), cancellationToken);
            Assert.True(child.IsFailure);
            return Result.Success();
        }
    }

    private sealed class SaveWithoutTransactionHandler(BusinessDbContext context)
        : IRequestHandler<SaveWithoutTransaction, bool>
    {
        public async ValueTask<Result<bool>> HandleAsync(SaveWithoutTransaction request,
            CancellationToken cancellationToken = default)
        {
            var hadTransaction = context.Database.CurrentTransaction is not null;
            context.Records.Add(new BusinessRecord { Id = Guid.NewGuid(), Name = request.Name });
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(hadTransaction);
        }
    }

    private sealed class RecordCreatedHandler(EventRecorder recorder) : IEventHandler<RecordCreated>
    {
        public ValueTask<Result> HandleAsync(RecordCreated @event, CancellationToken cancellationToken = default)
        {
            recorder.Record(@event.Name);
            return new ValueTask<Result>(Result.Success());
        }
    }
}
