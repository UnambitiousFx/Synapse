using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Tests.Definitions;

namespace UnambitiousFx.Synapse.Tests;

public sealed class TypedRequestPipelineBehaviorTests
{
    [Fact]
    public async Task Typed_behavior_without_response_executes_only_for_matching_request()
    {
        var services = new ServiceCollection();
        services.AddSynapse(cfg =>
        {
            cfg.RegisterRequestHandler<TypedSampleRequestHandler, TypedSampleRequest>();
            cfg.RegisterRequestPipelineBehavior<OnlyTypedSampleRequestBehavior, TypedSampleRequest>();
        });
        var provider = services.BuildServiceProvider();
        var behavior = provider.GetRequiredService<OnlyTypedSampleRequestBehavior>();
        var sender = provider.GetRequiredService<ISender>();

        await sender.SendAsync(new TypedSampleRequest());
        Assert.Equal(1, behavior.ExecutionCount);
    }

    [Fact]
    public async Task Typed_behavior_without_response_skips_for_request_with_response()
    {
        var services = new ServiceCollection();
        services.AddSynapse(cfg =>
        {
            cfg.RegisterRequestHandler<TypedSampleRequestWithResponseHandler, TypedSampleRequestWithResponse, int>();
            cfg.RegisterRequestPipelineBehavior<OnlyTypedSampleRequestBehavior, TypedSampleRequest>();
        });
        var provider = services.BuildServiceProvider();
        var behavior = provider.GetRequiredService<OnlyTypedSampleRequestBehavior>();
        var sender = provider.GetRequiredService<ISender>();

        await sender.SendAsync<TypedSampleRequestWithResponse, int>(new TypedSampleRequestWithResponse(42));
        Assert.Equal(0, behavior.ExecutionCount);
    }

    [Fact]
    public async Task Typed_behavior_with_response_executes_only_for_matching_request()
    {
        var services = new ServiceCollection();
        services.AddSynapse(cfg =>
        {
            cfg.RegisterRequestHandler<TypedSampleRequestWithResponseHandler, TypedSampleRequestWithResponse, int>();
            cfg.RegisterRequestPipelineBehavior<OnlyTypedSampleRequestWithResponseBehavior,
                TypedSampleRequestWithResponse, int>();
        });
        var provider = services.BuildServiceProvider();
        var behavior = provider.GetRequiredService<OnlyTypedSampleRequestWithResponseBehavior>();
        var sender = provider.GetRequiredService<ISender>();

        var result =
            await sender.SendAsync<TypedSampleRequestWithResponse, int>(new TypedSampleRequestWithResponse(42));
        Assert.True(result.TryGet(out int value));
        Assert.Equal(42, value);
        Assert.Equal(1, behavior.ExecutionCount);
    }

    [Fact]
    public async Task Interface_typed_behavior_executes_only_for_matching_request()
    {
        var services = new ServiceCollection();
        services.AddSynapse(cfg =>
        {
            cfg.RegisterRequestHandler<TypedSampleInheritanceRequestHandler, TypedSampleInheritanceRequest>();
            cfg.RegisterRequestPipelineBehavior<InterfaceTypedRequestBehavior, TypedSampleInheritanceRequest>();
        });
        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();

        await sender.SendAsync(new TypedSampleInheritanceRequest());
        var behavior = provider.GetRequiredService<InterfaceTypedRequestBehavior>();
        Assert.Equal(1, behavior.ExecutionCount);
    }

    [Fact]
    public async Task Abstract_typed_behavior_executes_only_for_matching_request()
    {
        var services = new ServiceCollection();
        services.AddSynapse(cfg =>
        {
            cfg.RegisterRequestHandler<TypedSampleInheritanceRequestHandler, TypedSampleInheritanceRequest>();
            cfg.RegisterRequestPipelineBehavior<AbstractTypedRequestBehavior, TypedSampleInheritanceRequest>();
        });
        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();

        await sender.SendAsync(new TypedSampleInheritanceRequest());
        var behavior = provider.GetRequiredService<AbstractTypedRequestBehavior>();
        Assert.Equal(1, behavior.ExecutionCount);
    }

    [Fact]
    public async Task Conditional_typed_behavior_executes_when_predicate_true()
    {
        var services = new ServiceCollection();
        services.AddSynapse(cfg =>
        {
            cfg.RegisterRequestHandler<TypedSampleRequestHandler, TypedSampleRequest>();
            cfg.RegisterConditionalRequestPipelineBehavior<ConditionalTypedRequestBehavior, TypedSampleRequest>(_ =>
                true);
        });
        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();

        await sender.SendAsync(new TypedSampleRequest());
        var behavior = provider.GetRequiredService<ConditionalTypedRequestBehavior>();
        Assert.Equal(1, behavior.ExecutionCount);
    }

    [Fact]
    public async Task Conditional_typed_behavior_skips_when_predicate_false()
    {
        var services = new ServiceCollection();
        services.AddSynapse(cfg =>
        {
            cfg.RegisterRequestHandler<TypedSampleRequestHandler, TypedSampleRequest>();
            cfg.RegisterConditionalRequestPipelineBehavior<ConditionalTypedRequestBehavior, TypedSampleRequest>(_ =>
                false);
        });
        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();

        await sender.SendAsync(new TypedSampleRequest());
        var behavior = provider.GetRequiredService<ConditionalTypedRequestBehavior>();
        Assert.Equal(0, behavior.ExecutionCount);
    }

    private sealed class TypedSampleRequestHandler : IRequestHandler<TypedSampleRequest>
    {
        public ValueTask<Result> HandleAsync(TypedSampleRequest request,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<Result>(Result.Success());
        }
    }

    private sealed class TypedSampleRequestWithResponseHandler : IRequestHandler<TypedSampleRequestWithResponse, int>
    {
        public ValueTask<Result<int>> HandleAsync(TypedSampleRequestWithResponse request,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<Result<int>>(Result.Success(request.Value));
        }
    }

    private sealed class TypedSampleInheritanceRequestHandler : IRequestHandler<TypedSampleInheritanceRequest>
    {
        public ValueTask<Result> HandleAsync(TypedSampleInheritanceRequest request,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<Result>(Result.Success());
        }
    }
}