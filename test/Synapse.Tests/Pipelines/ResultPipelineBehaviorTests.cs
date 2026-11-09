using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Functional;
using UnambitiousFx.Functional.Failures;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Tests.Pipelines;

[TestSubject(typeof(IResultPipelineBehavior<,>))]
public sealed class ResultPipelineBehaviorTests
{
    [Fact]
    public async Task InvokeAsync_WithOneOpenGenericBehavior_ShortCircuitsBothRequestShapes()
    {
        // Arrange (Given)
        var log = new List<string>();
        await using var provider = BuildProvider(log, cfg =>
            cfg.AddOpenGenericResultPipelineBehavior(typeof(RejectBehavior<,>)));
        await using var scope = provider.CreateAsyncScope();
        var invoker = scope.ServiceProvider.GetRequiredService<IInvoker>();

        // Act (When)
        var command = await invoker.InvokeAsync(new Command(), TestContext.Current.CancellationToken);
        var query = await invoker.InvokeAsync(new Query(), TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(command.TryGetFailure(out var commandFailure));
        Assert.Equal("rejected", commandFailure.Message);
        Assert.True(query.TryGetFailure(out var queryFailure));
        Assert.Equal("rejected", queryFailure.Message);
        Assert.Empty(log);
    }

    [Fact]
    public async Task InvokeAsync_WithClosedRegistrations_ReadsTheResultAfterTheHandler()
    {
        // Arrange (Given)
        var log = new List<string>();
        await using var provider = BuildProvider(log, cfg =>
        {
            cfg.RegisterResultPipelineBehavior<RecordingBehavior<Command, Result>, Command>();
            cfg.RegisterResultPipelineBehavior<RecordingBehavior<Query, Result<int>>, Query, int>();
        });
        await using var scope = provider.CreateAsyncScope();
        var invoker = scope.ServiceProvider.GetRequiredService<IInvoker>();

        // Act (When)
        await invoker.InvokeAsync(new Command(), TestContext.Current.CancellationToken);
        var query = await invoker.InvokeAsync(new Query(), TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.Equal(["handler:Command", "after:Command:True", "handler:Query", "after:Query:True"], log);
        Assert.True(query.TryGetValue(out var value));
        Assert.Equal(42, value);
    }

    [Fact]
    public async Task InvokeAsync_WithTypedAndResultBehaviors_RunsThemInOneOrderedChain()
    {
        // Arrange (Given) — registered in scrambled order; runtime order must follow IOrderedPipelineBehavior
        var log = new List<string>();
        await using var provider = BuildProvider(log, cfg =>
        {
            cfg.RegisterResultPipelineBehavior<LastResultBehavior<Command, Result>, Command>();
            cfg.RegisterRequestPipelineBehavior<MiddleTypedBehavior, Command>();
            cfg.RegisterResultPipelineBehavior<FirstResultBehavior<Command, Result>, Command>();
        });
        await using var scope = provider.CreateAsyncScope();
        var invoker = scope.ServiceProvider.GetRequiredService<IInvoker>();

        // Act (When)
        await invoker.InvokeAsync(new Command(), TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.Equal(["first", "middle", "last", "handler:Command"], log);
    }

    [Fact]
    public async Task HandleAsync_WithAHandBuiltNext_CanBeUnitTested()
    {
        // Arrange (Given)
        var behavior = new RejectBehavior<Query, Result<int>>();
        var next = new RequestNext<Query, Result<int>>((_, _) => new ValueTask<Result<int>>(Result.Success(1)),
            Result.Failure<int>);

        // Act (When)
        var result = await behavior.HandleAsync(new Query(), next, TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsFailure);
    }

    private static ServiceProvider BuildProvider(List<string> log, Action<ISynapseConfig> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddSynapse(cfg =>
        {
            cfg.RegisterRequestHandler<CommandHandler, Command>();
            cfg.RegisterRequestHandler<QueryHandler, Query, int>();
            configure(cfg);
        });
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    public sealed record Command : IRequest;

    public sealed record Query : IRequest<int>;

    private sealed class CommandHandler(List<string> log) : IRequestHandler<Command>
    {
        public ValueTask<Result> HandleAsync(Command request, CancellationToken cancellationToken = default)
        {
            log.Add("handler:Command");
            return new ValueTask<Result>(Result.Success());
        }
    }

    private sealed class QueryHandler(List<string> log) : IRequestHandler<Query, int>
    {
        public ValueTask<Result<int>> HandleAsync(Query request, CancellationToken cancellationToken = default)
        {
            log.Add("handler:Query");
            return new ValueTask<Result<int>>(Result.Success(42));
        }
    }

    public sealed class RejectBehavior<TRequest, TResult> : IResultPipelineBehavior<TRequest, TResult>
        where TResult : IResult
    {
        public ValueTask<TResult> HandleAsync(TRequest request,
            RequestNext<TRequest, TResult> next,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<TResult>(next.Fail(new Failure("rejected")));
        }
    }

    public sealed class RecordingBehavior<TRequest, TResult>(List<string> log)
        : IResultPipelineBehavior<TRequest, TResult>
        where TResult : IResult
    {
        public async ValueTask<TResult> HandleAsync(TRequest request,
            RequestNext<TRequest, TResult> next,
            CancellationToken cancellationToken = default)
        {
            var result = await next.InvokeAsync(request, cancellationToken);
            log.Add($"after:{typeof(TRequest).Name}:{result.IsSuccess}");
            return result;
        }
    }

    public sealed class FirstResultBehavior<TRequest, TResult>(List<string> log)
        : IResultPipelineBehavior<TRequest, TResult>, IOrderedPipelineBehavior
        where TResult : IResult
    {
        public uint Order => IOrderedPipelineBehavior.First;

        public ValueTask<TResult> HandleAsync(TRequest request,
            RequestNext<TRequest, TResult> next,
            CancellationToken cancellationToken = default)
        {
            log.Add("first");
            return next.InvokeAsync(request, cancellationToken);
        }
    }

    public sealed class LastResultBehavior<TRequest, TResult>(List<string> log)
        : IResultPipelineBehavior<TRequest, TResult>, IOrderedPipelineBehavior
        where TResult : IResult
    {
        public uint Order => IOrderedPipelineBehavior.Last;

        public ValueTask<TResult> HandleAsync(TRequest request,
            RequestNext<TRequest, TResult> next,
            CancellationToken cancellationToken = default)
        {
            log.Add("last");
            return next.InvokeAsync(request, cancellationToken);
        }
    }

    private sealed class MiddleTypedBehavior(List<string> log)
        : IRequestPipelineBehavior<Command>, IOrderedPipelineBehavior
    {
        public uint Order => IOrderedPipelineBehavior.Middle;

        public ValueTask<Result> HandleAsync(Command request,
            RequestHandlerDelegate<Command> next,
            CancellationToken cancellationToken = default)
        {
            log.Add("middle");
            return next(request, cancellationToken);
        }
    }
}
