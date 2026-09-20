using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Pipelines;
using UnambitiousFx.Synapse.Publish;
using UnambitiousFx.Synapse.Tests.Definitions;

namespace UnambitiousFx.Synapse.Tests.Pipelines;

/// <summary>
///     Known issue #93: the in-memory outbox is not enlisted in any transaction, so the events of a command that
///     failed stayed in it and were dispatched by the next unrelated commit.
/// </summary>
[TestSubject(typeof(OutboxDiscardOnFailureBehavior<>))]
public sealed class OutboxDiscardOnFailureBehaviorTests
{
    [Fact]
    public async Task InvokeAsync_WhenCommandFailsAfterEmitting_DoesNotDispatchItsEventOnALaterCommit()
    {
        // Arrange (Given)
        var recorder = new EventRecorder();
        await using var provider = BuildProvider(recorder, cfg => cfg.RegisterOutboxDiscardOnFailure<FailingCommand>());

        // Act (When)
        var failed = await Invoke(provider, new FailingCommand("failed-command"));
        await Invoke(provider, new CommitCommand());

        // Assert (Then)
        Assert.True(failed.IsFailure);
        Assert.Empty(recorder.Dispatched);
    }

    [Fact]
    public async Task InvokeAsync_WhenCommandThrowsAfterEmitting_DiscardsItsEventAndRethrows()
    {
        // Arrange (Given)
        var recorder = new EventRecorder();
        await using var provider = BuildProvider(recorder,
            cfg => cfg.RegisterOutboxDiscardOnFailure<ThrowingCommand, int>());

        // Act (When)
        var exception = await Record.ExceptionAsync(async () =>
        {
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IInvoker>()
                .InvokeAsync(new ThrowingCommand("thrown-command"), TestContext.Current.CancellationToken);
        });
        await Invoke(provider, new CommitCommand());

        // Assert (Then)
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Empty(recorder.Dispatched);
    }

    [Fact]
    public async Task InvokeAsync_WhenCommandSucceeds_KeepsItsEventForALaterCommit()
    {
        // Arrange (Given)
        var recorder = new EventRecorder();
        await using var provider = BuildProvider(recorder,
            cfg => cfg.RegisterOutboxDiscardOnFailure<EmittingCommand>());

        // Act (When)
        var emitted = await Invoke(provider, new EmittingCommand("kept-event"));
        await Invoke(provider, new CommitCommand());

        // Assert (Then)
        Assert.True(emitted.IsSuccess);
        Assert.Equal(["kept-event"], recorder.Dispatched);
    }

    [Fact]
    public async Task InvokeAsync_WhenAnotherRequestFailed_DoesNotDiscardWhatItStored()
    {
        // Arrange (Given) — the failing request runs in its own scope, so it must only take back its own events
        var recorder = new EventRecorder();
        await using var provider = BuildProvider(recorder, cfg =>
        {
            cfg.RegisterOutboxDiscardOnFailure<FailingCommand>();
            cfg.RegisterOutboxDiscardOnFailure<EmittingCommand>();
        });
        await Invoke(provider, new EmittingCommand("survivor"));

        // Act (When)
        await Invoke(provider, new FailingCommand("failed-command"));
        await Invoke(provider, new CommitCommand());

        // Assert (Then)
        Assert.Equal(["survivor"], recorder.Dispatched);
    }

    [Fact]
    public async Task InvokeAsync_WithoutTheBehavior_DispatchesTheFailedCommandsEventOnALaterCommit()
    {
        // Arrange (Given) — documents that the behavior is opt-in, and what it is opting out of
        var recorder = new EventRecorder();
        await using var provider = BuildProvider(recorder, _ => { });

        // Act (When)
        await Invoke(provider, new FailingCommand("leaked"));
        await Invoke(provider, new CommitCommand());

        // Assert (Then)
        Assert.Equal(["leaked"], recorder.Dispatched);
    }

    private static ServiceProvider BuildProvider(EventRecorder recorder, Action<ISynapseConfig> configure)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton(recorder);
        services.AddSynapse(cfg =>
        {
            cfg.RegisterRequestHandler<FailingCommandHandler, FailingCommand>();
            cfg.RegisterRequestHandler<ThrowingCommandHandler, ThrowingCommand, int>();
            cfg.RegisterRequestHandler<EmittingCommandHandler, EmittingCommand>();
            cfg.RegisterRequestHandler<CommitCommandHandler, CommitCommand>();
            cfg.RegisterEventHandler<RecordingEventHandler, EventExample>();
            configure(cfg);
        });
        return services.BuildServiceProvider();
    }

    // One scope per invocation, as one request would have.
    private static async Task<Result> Invoke<TRequest>(IServiceProvider provider, TRequest request)
        where TRequest : IRequest
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IInvoker>()
            .InvokeAsync(request, TestContext.Current.CancellationToken);
    }

    private sealed class EventRecorder
    {
        public List<string> Dispatched { get; } = [];
    }

    private sealed class RecordingEventHandler(EventRecorder recorder) : IEventHandler<EventExample>
    {
        public ValueTask<Result> HandleAsync(EventExample @event, CancellationToken cancellationToken = default)
        {
            recorder.Dispatched.Add(@event.Name);
            return new ValueTask<Result>(Result.Success());
        }
    }

    private sealed record FailingCommand(string EventName) : IRequest;

    private sealed class FailingCommandHandler(IEmitter emitter) : IRequestHandler<FailingCommand>
    {
        public async ValueTask<Result> HandleAsync(FailingCommand request, CancellationToken cancellationToken = default)
        {
            await emitter.EmitAsync(new EventExample(request.EventName), EmitMode.Outbox, cancellationToken);
            return Result.Failure("the command failed after emitting");
        }
    }

    private sealed record ThrowingCommand(string EventName) : IRequest<int>;

    private sealed class ThrowingCommandHandler(IEmitter emitter) : IRequestHandler<ThrowingCommand, int>
    {
        public async ValueTask<Result<int>> HandleAsync(ThrowingCommand request,
            CancellationToken cancellationToken = default)
        {
            await emitter.EmitAsync(new EventExample(request.EventName), EmitMode.Outbox, cancellationToken);
            throw new InvalidOperationException("the command threw after emitting");
        }
    }

    private sealed record EmittingCommand(string EventName) : IRequest;

    private sealed class EmittingCommandHandler(IEmitter emitter) : IRequestHandler<EmittingCommand>
    {
        public async ValueTask<Result> HandleAsync(EmittingCommand request, CancellationToken cancellationToken = default)
        {
            await emitter.EmitAsync(new EventExample(request.EventName), EmitMode.Outbox, cancellationToken);
            return Result.Success();
        }
    }

    private sealed record CommitCommand : IRequest;

    private sealed class CommitCommandHandler(IOutboxCommit outboxCommit) : IRequestHandler<CommitCommand>
    {
        public ValueTask<Result> HandleAsync(CommitCommand request, CancellationToken cancellationToken = default)
        {
            return outboxCommit.CommitAsync(cancellationToken);
        }
    }
}
