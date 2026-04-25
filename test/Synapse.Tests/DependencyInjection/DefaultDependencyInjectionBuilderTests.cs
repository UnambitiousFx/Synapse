using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Tests.DependencyInjection;

public sealed class DefaultDependencyInjectionBuilderTests
{
    [Fact]
    public void Apply_WithUnconditionalRegistrations_RegistersExpectedServices()
    {
        // Arrange (Given)
        var services = new ServiceCollection();
        var builder = new DefaultDependencyInjectionBuilder();

        builder.RegisterRequestHandler<TestRequestWithResponseHandler, TestRequestWithResponse, int>();
        builder.RegisterRequestHandler<TestRequestHandler, TestRequest>();
        builder.RegisterEventHandler<TestEventHandler, TestEvent>();
        builder.RegisterStreamRequestHandler<TestStreamRequestHandler, TestStreamRequest, int>();

        // Act (When)
        builder.Apply(services);

        // Assert (Then)
        Assert.Contains(services, x =>
            x.ServiceType == typeof(IRequestHandler<TestRequestWithResponse, int>) &&
            x.ImplementationType == typeof(ProxyRequestHandler<TestRequestWithResponseHandler, TestRequestWithResponse, int>));
        Assert.Contains(services, x =>
            x.ServiceType == typeof(IRequestHandler<TestRequest>) &&
            x.ImplementationType == typeof(ProxyRequestHandler<TestRequestHandler, TestRequest>));
        Assert.Contains(services, x =>
            x.ServiceType == typeof(IEventHandler<TestEvent>) &&
            x.ImplementationType == typeof(TestEventHandler));
        Assert.Contains(services, x =>
            x.ServiceType == typeof(IStreamRequestHandler<TestStreamRequest, int>) &&
            x.ImplementationType == typeof(ProxyStreamRequestHandler<TestStreamRequestHandler, TestStreamRequest, int>));
    }

    [Fact]
    public void Apply_WithConditionalRegistrations_OnlyRegistersWhenConditionIsTrue()
    {
        // Arrange (Given)
        var services = new ServiceCollection();
        var builder = new DefaultDependencyInjectionBuilder();

        builder.RegisterRequestHandlerWhen<TestRequestWithResponseHandler, TestRequestWithResponse, int>(() => true);
        builder.RegisterRequestHandlerWhen<TestRequestHandler, TestRequest>(() => false);
        builder.RegisterEventHandlerWhen<TestEventHandler, TestEvent>(() => true);
        builder.RegisterStreamRequestHandlerWhen<TestStreamRequestHandler, TestStreamRequest, int>(() => false);

        // Act (When)
        builder.Apply(services);

        // Assert (Then)
        Assert.Contains(services, x =>
            x.ServiceType == typeof(IRequestHandler<TestRequestWithResponse, int>) &&
            x.ImplementationType == typeof(ProxyRequestHandler<TestRequestWithResponseHandler, TestRequestWithResponse, int>));
        Assert.DoesNotContain(services, x =>
            x.ServiceType == typeof(IRequestHandler<TestRequest>) &&
            x.ImplementationType == typeof(ProxyRequestHandler<TestRequestHandler, TestRequest>));
        Assert.Contains(services, x =>
            x.ServiceType == typeof(IEventHandler<TestEvent>) &&
            x.ImplementationType == typeof(TestEventHandler));
        Assert.DoesNotContain(services, x =>
            x.ServiceType == typeof(IStreamRequestHandler<TestStreamRequest, int>) &&
            x.ImplementationType == typeof(ProxyStreamRequestHandler<TestStreamRequestHandler, TestStreamRequest, int>));
    }

    private sealed record TestRequestWithResponse : IRequest<int>;

    private sealed class TestRequestWithResponseHandler : IRequestHandler<TestRequestWithResponse, int>
    {
        public ValueTask<Result<int>> HandleAsync(TestRequestWithResponse request, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Result.Success(1));
        }
    }

    private sealed record TestRequest : IRequest;

    private sealed class TestRequestHandler : IRequestHandler<TestRequest>
    {
        public ValueTask<Result> HandleAsync(TestRequest request, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Result.Success());
        }
    }

    private sealed record TestEvent : IEvent;

    private sealed class TestEventHandler : IEventHandler<TestEvent>
    {
        public ValueTask<Result> HandleAsync(TestEvent @event, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Result.Success());
        }
    }

    private sealed record TestStreamRequest : IStreamRequest<int>;

    private sealed class TestStreamRequestHandler : IStreamRequestHandler<TestStreamRequest, int>
    {
        public async IAsyncEnumerable<Result<int>> HandleAsync(TestStreamRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return Result.Success(1);
            await Task.CompletedTask;
        }
    }
}