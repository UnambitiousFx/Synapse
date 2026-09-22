using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Tests.Cqrs;

[TestSubject(typeof(ICommandHandler<,>))]
public sealed class CqrsMarkerDispatchTests
{
    [Fact]
    public async Task InvokeAsync_WithACommandHandledThroughICommandHandler_RunsTheHandler()
    {
        // Arrange (Given)
        var calls = new Calls();
        await using var provider = Build(calls, cfg => cfg.RegisterRequestHandler<CreateHandler, CreateCommand, int>());

        // Act (When)
        var result = await InvokeAsync(provider, new CreateCommand());

        // Assert (Then)
        Assert.True(result.IsSuccess);
        Assert.Equal(["CreateHandler"], calls.Items);
    }

    [Fact]
    public async Task InvokeAsync_WithAQueryHandledThroughIQueryHandler_RunsTheHandler()
    {
        // Arrange (Given)
        var calls = new Calls();
        await using var provider = Build(calls, cfg => cfg.RegisterRequestHandler<GetHandler, GetQuery, int>());

        // Act (When)
        var result = await InvokeAsync(provider, new GetQuery());

        // Assert (Then)
        Assert.True(result.IsSuccess);
        Assert.Equal(["GetHandler"], calls.Items);
    }

    [Fact]
    public async Task InvokeAsync_WithAVoidCommandHandledThroughICommandHandler_RunsTheHandler()
    {
        // Arrange (Given)
        var calls = new Calls();
        await using var provider = Build(calls, cfg => cfg.RegisterRequestHandler<SendHandler, SendCommand>());

        // Act (When)
        var result = await InvokeAsync(provider, new SendCommand());

        // Assert (Then)
        Assert.True(result.IsSuccess);
        Assert.Equal(["SendHandler"], calls.Items);
    }

    [Fact]
    public async Task InvokeAsync_WithABehaviorConstrainedToCommands_RunsItForACommandOnly()
    {
        // Arrange (Given)
        var calls = new Calls();
        await using var provider = Build(calls, cfg =>
        {
            cfg.RegisterRequestHandler<CreateHandler, CreateCommand, int>();
            cfg.RegisterRequestHandler<GetHandler, GetQuery, int>();
            cfg.AddOpenGenericRequestWithResponsePipelineBehavior(typeof(CommandOnlyBehavior<,>));
        });

        // Act (When)
        await InvokeAsync(provider, new CreateCommand());
        await InvokeAsync(provider, new GetQuery());

        // Assert (Then)
        Assert.Equal(["CommandOnlyBehavior<CreateCommand>", "CreateHandler", "GetHandler"], calls.Items);
    }

    [Fact]
    public async Task InvokeAsync_WithAVoidBehaviorConstrainedToCommands_RunsItForACommandOnly()
    {
        // Arrange (Given)
        var calls = new Calls();
        await using var provider = Build(calls, cfg =>
        {
            cfg.RegisterRequestHandler<SendHandler, SendCommand>();
            cfg.RegisterRequestHandler<PlainHandler, PlainRequest>();
            cfg.AddOpenGenericRequestPipelineBehavior(typeof(VoidCommandOnlyBehavior<>));
        });

        // Act (When)
        await InvokeAsync(provider, new SendCommand());
        await InvokeAsync(provider, new PlainRequest());

        // Assert (Then)
        Assert.Equal(["VoidCommandOnlyBehavior<SendCommand>", "SendHandler", "PlainHandler"], calls.Items);
    }

    [Fact]
    public async Task Describe_WithABehaviorConstrainedToCommands_ListsItOnTheCommandPipelineOnly()
    {
        // Arrange (Given)
        await using var provider = Build(new Calls(), cfg =>
        {
            cfg.RegisterRequestHandler<CreateHandler, CreateCommand, int>();
            cfg.RegisterRequestHandler<GetHandler, GetQuery, int>();
            cfg.AddOpenGenericRequestWithResponsePipelineBehavior(typeof(CommandOnlyBehavior<,>));
        });
        var describer = provider.GetRequiredService<IPipelineDescriber>();

        // Act (When)
        var command = describer.Describe<CreateCommand, int>();
        var query = describer.Describe<GetQuery, int>();

        // Assert (Then)
        Assert.NotNull(command);
        Assert.NotNull(query);
        Assert.Equal(typeof(CommandOnlyBehavior<CreateCommand, int>), Assert.Single(command.Behaviors).Type);
        Assert.Empty(query.Behaviors);
    }

    [Fact]
    public async Task ValidateSynapse_WithMarkerBasedHandlersAndConstrainedBehaviors_IsValidWithNoIssues()
    {
        // Arrange (Given)
        await using var provider = Build(new Calls(), cfg =>
        {
            cfg.RegisterRequestHandler<CreateHandler, CreateCommand, int>();
            cfg.RegisterRequestHandler<GetHandler, GetQuery, int>();
            cfg.RegisterRequestHandler<SendHandler, SendCommand>();
            cfg.AddOpenGenericRequestWithResponsePipelineBehavior(typeof(CommandOnlyBehavior<,>));
        });

        // Act (When)
        var report = provider.ValidateSynapse();

        // Assert (Then)
        Assert.True(report.IsValid);
        Assert.Empty(report.Issues);
    }

    private static ServiceProvider Build(Calls calls, Action<ISynapseConfig> configure)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton(calls);
        services.AddSynapse(configure);
        return services.BuildServiceProvider();
    }

    private static async Task<Result<int>> InvokeAsync<TRequest>(IServiceProvider provider, TRequest request)
        where TRequest : IRequest<int>
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IInvoker>()
            .InvokeAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<Result> InvokeAsync(IServiceProvider provider, IRequest request)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IInvoker>()
            .InvokeAsync(request, TestContext.Current.CancellationToken);
    }

    private sealed class Calls
    {
        public List<string> Items { get; } = [];
    }

    private sealed record CreateCommand : ICommand<int>;

    private sealed record GetQuery : IQuery<int>;

    private sealed record SendCommand : ICommand;

    private sealed record PlainRequest : IRequest;

    private sealed class CreateHandler(Calls calls) : ICommandHandler<CreateCommand, int>
    {
        public ValueTask<Result<int>> HandleAsync(CreateCommand request, CancellationToken cancellationToken = default)
        {
            calls.Items.Add(nameof(CreateHandler));
            return ValueTask.FromResult(Result.Success(1));
        }
    }

    private sealed class GetHandler(Calls calls) : IQueryHandler<GetQuery, int>
    {
        public ValueTask<Result<int>> HandleAsync(GetQuery request, CancellationToken cancellationToken = default)
        {
            calls.Items.Add(nameof(GetHandler));
            return ValueTask.FromResult(Result.Success(2));
        }
    }

    private sealed class SendHandler(Calls calls) : ICommandHandler<SendCommand>
    {
        public ValueTask<Result> HandleAsync(SendCommand request, CancellationToken cancellationToken = default)
        {
            calls.Items.Add(nameof(SendHandler));
            return ValueTask.FromResult(Result.Success());
        }
    }

    private sealed class PlainHandler(Calls calls) : IRequestHandler<PlainRequest>
    {
        public ValueTask<Result> HandleAsync(PlainRequest request, CancellationToken cancellationToken = default)
        {
            calls.Items.Add(nameof(PlainHandler));
            return ValueTask.FromResult(Result.Success());
        }
    }

    private sealed class CommandOnlyBehavior<TRequest, TResponse>(Calls calls)
        : IRequestPipelineBehavior<TRequest, TResponse>
        where TRequest : ICommand<TResponse>
        where TResponse : notnull
    {
        public ValueTask<Result<TResponse>> HandleAsync(TRequest request,
            RequestHandlerDelegate<TRequest, TResponse> next, CancellationToken cancellationToken = default)
        {
            calls.Items.Add($"CommandOnlyBehavior<{typeof(TRequest).Name}>");
            return next(request, cancellationToken);
        }
    }

    private sealed class VoidCommandOnlyBehavior<TRequest>(Calls calls) : IRequestPipelineBehavior<TRequest>
        where TRequest : ICommand
    {
        public ValueTask<Result> HandleAsync(TRequest request, RequestHandlerDelegate<TRequest> next,
            CancellationToken cancellationToken = default)
        {
            calls.Items.Add($"VoidCommandOnlyBehavior<{typeof(TRequest).Name}>");
            return next(request, cancellationToken);
        }
    }
}
