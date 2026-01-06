using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Tests.Definitions;

namespace UnambitiousFx.Synapse.Tests;

public sealed class ConditionalRequestPipelineBehaviorTests
{
    [Fact]
    public async Task Conditional_untyped_behavior_executes_when_predicate_true()
    {
        var services = new ServiceCollection();
        services.AddSynapse(cfg =>
        {
            cfg.RegisterRequestHandler<RequestExampleHandler, RequestExample>();
            cfg.RegisterConditionalRequestPipelineBehavior<UntypedConditionalBehavior>(_ => true);
        });
        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();
        await sender.SendAsync(new RequestExample());
        var behavior = provider.GetRequiredService<UntypedConditionalBehavior>();
        Assert.Equal(1, behavior.ExecutionCount);
    }

    [Fact]
    public async Task Conditional_untyped_behavior_skips_when_predicate_false()
    {
        var services = new ServiceCollection();
        services.AddSynapse(cfg =>
        {
            cfg.RegisterRequestHandler<RequestExampleHandler, RequestExample>();
            cfg.RegisterConditionalRequestPipelineBehavior<UntypedConditionalBehavior>(_ => false);
        });
        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();
        await sender.SendAsync(new RequestExample());
        var behavior = provider.GetRequiredService<UntypedConditionalBehavior>();
        Assert.Equal(0, behavior.ExecutionCount);
    }

    private sealed class UntypedConditionalBehavior : IRequestPipelineBehavior
    {
        public int ExecutionCount { get; private set; }

        public ValueTask<Result> HandleAsync<TRequest>(TRequest request,
            RequestHandlerDelegate next,
            CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            ExecutionCount++;
            return next();
        }

        public ValueTask<Result<TResponse>> HandleAsync<TRequest, TResponse>(TRequest request,
            RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken = default)
            where TResponse : notnull
            where TRequest : IRequest<TResponse>
        {
            ExecutionCount++;
            return next();
        }
    }
}