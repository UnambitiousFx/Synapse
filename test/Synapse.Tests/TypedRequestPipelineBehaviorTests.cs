using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Tests.Definitions;

namespace UnambitiousFx.Synapse.Tests;

public sealed class TypedRequestPipelineBehaviorTests
{
    [Fact]
    public async Task Typed_behavior_without_response_executes_only_for_registered_request()
    {
        // Arrange (Given)
        var services = new ServiceCollection();
        services.AddSynapse(cfg =>
        {
            cfg.RegisterRequestHandler<TypedSampleRequestHandler, TypedSampleRequest>();
            cfg.RegisterRequestPipelineBehavior<OnlyTypedSampleRequestBehavior, TypedSampleRequest>();
        });
        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<IInvoker>();

        // Resolve via interface so we get the same instance the pipeline will use
        var behavior = provider.GetServices<IRequestPipelineBehavior<TypedSampleRequest>>()
            .OfType<OnlyTypedSampleRequestBehavior>().Single();

        // Act (When)
        await sender.InvokeAsync(new TypedSampleRequest());

        // Assert (Then)
        Assert.Equal(1, behavior.ExecutionCount);
    }

    [Fact]
    public async Task Typed_behavior_without_response_is_absent_from_unrelated_request_resolution()
    {
        // Arrange (Given) — behavior for TypedSampleRequest only; handler for TypedSampleRequestWithResponse
        var services = new ServiceCollection();
        services.AddSynapse(cfg =>
        {
            cfg.RegisterRequestHandler<TypedSampleRequestWithResponseHandler, TypedSampleRequestWithResponse, int>();
            // NOT registered for TypedSampleRequestWithResponse — DI will not return it
        });
        services.AddScoped<OnlyTypedSampleRequestBehavior>();
        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<IInvoker>();

        // Act (When) — dispatch a with-response request
        await sender.InvokeAsync(new TypedSampleRequestWithResponse(42));

        // Assert (Then) — behavior for TypedSampleRequest was NOT resolved for this handler
        var behaviorsForWithResponse =
            provider.GetServices<IRequestPipelineBehavior<TypedSampleRequestWithResponse, int>>();
        Assert.Empty(behaviorsForWithResponse);
    }

    [Fact]
    public async Task Typed_behavior_with_response_executes_only_for_matching_request()
    {
        // Arrange (Given)
        var services = new ServiceCollection();
        services.AddSynapse(cfg =>
        {
            cfg.RegisterRequestHandler<TypedSampleRequestWithResponseHandler, TypedSampleRequestWithResponse, int>();
            cfg.RegisterRequestPipelineBehavior<OnlyTypedSampleRequestWithResponseBehavior,
                TypedSampleRequestWithResponse, int>();
        });
        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<IInvoker>();

        // Resolve via interface so we get the same instance the pipeline will use
        var behavior = provider.GetServices<IRequestPipelineBehavior<TypedSampleRequestWithResponse, int>>()
            .OfType<OnlyTypedSampleRequestWithResponseBehavior>().Single();

        // Act (When)
        var result = await sender.InvokeAsync(new TypedSampleRequestWithResponse(42));

        // Assert (Then)
        Assert.True(result.TryGet(out var value, out _));
        Assert.Equal(42, value);
        Assert.Equal(1, behavior.ExecutionCount);
    }

    [Fact]
    public async Task Two_behaviors_for_same_request_both_execute_in_registration_order()
    {
        // Arrange (Given)
        var executionOrder = new List<string>();
        var services = new ServiceCollection();
        // Register the shared list so DI can inject it into behavior constructors
        services.AddSingleton(executionOrder);
        services.AddSynapse(cfg =>
        {
            cfg.RegisterRequestHandler<TypedSampleRequestHandler, TypedSampleRequest>();
            cfg.RegisterRequestPipelineBehavior<FirstBehavior, TypedSampleRequest>();
            cfg.RegisterRequestPipelineBehavior<SecondBehavior, TypedSampleRequest>();
        });
        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<IInvoker>();

        // Act (When)
        await sender.InvokeAsync(new TypedSampleRequest());

        // Assert (Then)
        Assert.Equal(["First", "Second"], executionOrder);
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

    private sealed class FirstBehavior(List<string> log) : IRequestPipelineBehavior<TypedSampleRequest>
    {
        public ValueTask<Result> HandleAsync(TypedSampleRequest request,
            RequestHandlerDelegate<TypedSampleRequest> next,
            CancellationToken cancellationToken = default)
        {
            log.Add("First");
            return next(request, cancellationToken);
        }
    }

    private sealed class SecondBehavior(List<string> log) : IRequestPipelineBehavior<TypedSampleRequest>
    {
        public ValueTask<Result> HandleAsync(TypedSampleRequest request,
            RequestHandlerDelegate<TypedSampleRequest> next,
            CancellationToken cancellationToken = default)
        {
            log.Add("Second");
            return next(request, cancellationToken);
        }
    }
}
