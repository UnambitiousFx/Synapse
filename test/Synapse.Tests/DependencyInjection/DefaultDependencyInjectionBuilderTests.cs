using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Resolvers;

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

    [Fact]
    public void RegisterRequestHandler_WithResponse_PopulatesRequestDispatchers()
    {
        // Arrange (Given)
        var builder = new DefaultDependencyInjectionBuilder();

        // Act (When)
        builder.RegisterRequestHandler<TestRequestWithResponseHandler, TestRequestWithResponse, int>();

        // Assert (Then)
        Assert.Contains(typeof(TestRequestWithResponse), builder.RequestDispatchers.Keys);
    }

    [Fact]
    public void RegisterRequestHandler_WithoutResponse_DoesNotPopulateRequestDispatchers()
    {
        // Arrange (Given)
        var builder = new DefaultDependencyInjectionBuilder();

        // Act (When)
        builder.RegisterRequestHandler<TestRequestHandler, TestRequest>();

        // Assert (Then)
        Assert.DoesNotContain(typeof(TestRequest), builder.RequestDispatchers.Keys);
    }

    [Fact]
    public void RegisterEventHandler_PopulatesEventDispatchers()
    {
        // Arrange (Given)
        var builder = new DefaultDependencyInjectionBuilder();

        // Act (When)
        builder.RegisterEventHandler<TestEventHandler, TestEvent>();

        // Assert (Then)
        Assert.Contains(typeof(TestEvent), builder.EventDispatchers.Keys);
    }

    [Fact]
    public void RegisterStreamRequestHandler_PopulatesStreamRequestDispatchers()
    {
        // Arrange (Given)
        var builder = new DefaultDependencyInjectionBuilder();

        // Act (When)
        builder.RegisterStreamRequestHandler<TestStreamRequestHandler, TestStreamRequest, int>();

        // Assert (Then)
        Assert.Contains(typeof(TestStreamRequest), builder.StreamRequestDispatchers.Keys);
    }

    [Fact]
    public void RegisterRequestHandler_MultipleCallsSameType_DoesNotDuplicateDispatchers()
    {
        // Arrange (Given)
        var builder = new DefaultDependencyInjectionBuilder();

        // Act (When)
        builder.RegisterRequestHandler<TestRequestWithResponseHandler, TestRequestWithResponse, int>();
        builder.RegisterRequestHandler<TestRequestWithResponseHandler, TestRequestWithResponse, int>();

        // Assert (Then)
        Assert.Single(builder.RequestDispatchers);
    }

    [Fact]
    public void RegisterRequestHandlerWhen_ConditionTrue_PopulatesRequestDispatchersAfterApply()
    {
        // Arrange (Given)
        var services = new ServiceCollection();
        var builder = new DefaultDependencyInjectionBuilder();
        builder.RegisterRequestHandlerWhen<TestRequestWithResponseHandler, TestRequestWithResponse, int>(() => true);

        // Act (When)
        builder.Apply(services);

        // Assert (Then)
        Assert.Contains(typeof(TestRequestWithResponse), builder.RequestDispatchers.Keys);
    }

    [Fact]
    public void RegisterRequestHandlerWhen_ConditionFalse_DoesNotPopulateRequestDispatchersAfterApply()
    {
        // Arrange (Given)
        var services = new ServiceCollection();
        var builder = new DefaultDependencyInjectionBuilder();
        builder.RegisterRequestHandlerWhen<TestRequestWithResponseHandler, TestRequestWithResponse, int>(() => false);

        // Act (When)
        builder.Apply(services);

        // Assert (Then)
        Assert.DoesNotContain(typeof(TestRequestWithResponse), builder.RequestDispatchers.Keys);
    }

    // ── Pipeline behavior registration methods ───────────────────────────────

    [Fact]
    public void RegisterRequestPipelineBehavior_NoResponse_RegistersBehaviorInServiceCollection()
    {
        // Arrange (Given)
        var services = new ServiceCollection();
        var builder = new DefaultDependencyInjectionBuilder();

        // Act (When)
        builder.RegisterRequestPipelineBehavior<TestNoResponseBehavior, TestRequest>();
        builder.Apply(services);

        // Assert (Then)
        Assert.Contains(services, x =>
            x.ServiceType == typeof(IRequestPipelineBehavior<TestRequest>) &&
            x.ImplementationType == typeof(TestNoResponseBehavior));
    }

    [Fact]
    public void RegisterRequestPipelineBehavior_WithResponse_RegistersBehaviorInServiceCollection()
    {
        // Arrange (Given)
        var services = new ServiceCollection();
        var builder = new DefaultDependencyInjectionBuilder();

        // Act (When)
        builder.RegisterRequestPipelineBehavior<TestWithResponseBehavior, TestRequestWithResponse, int>();
        builder.Apply(services);

        // Assert (Then)
        Assert.Contains(services, x =>
            x.ServiceType == typeof(IRequestPipelineBehavior<TestRequestWithResponse, int>) &&
            x.ImplementationType == typeof(TestWithResponseBehavior));
    }

    [Fact]
    public void RegisterEventPipelineBehavior_RegistersBehaviorInServiceCollection()
    {
        // Arrange (Given)
        var services = new ServiceCollection();
        var builder = new DefaultDependencyInjectionBuilder();

        // Act (When)
        builder.RegisterEventPipelineBehavior<TestEventBehavior, TestEvent>();
        builder.Apply(services);

        // Assert (Then)
        Assert.Contains(services, x =>
            x.ServiceType == typeof(IEventPipelineBehavior<TestEvent>) &&
            x.ImplementationType == typeof(TestEventBehavior));
    }

    [Fact]
    public void RegisterStreamRequestPipelineBehavior_RegistersBehaviorInServiceCollection()
    {
        // Arrange (Given)
        var services = new ServiceCollection();
        var builder = new DefaultDependencyInjectionBuilder();

        // Act (When)
        builder.RegisterStreamRequestPipelineBehavior<TestStreamBehavior, TestStreamRequest, int>();
        builder.Apply(services);

        // Assert (Then)
        Assert.Contains(services, x =>
            x.ServiceType == typeof(IStreamRequestPipelineBehavior<TestStreamRequest, int>) &&
            x.ImplementationType == typeof(TestStreamBehavior));
    }

    // ── Dispatcher delegate invocation ───────────────────────────────────────

    [Fact]
    public async Task RegisterRequestHandler_WithResponse_DispatcherDelegate_InvokesHandler()
    {
        // Arrange (Given)
        var builder = new DefaultDependencyInjectionBuilder();
        builder.RegisterRequestHandler<TestRequestWithResponseHandler, TestRequestWithResponse, int>();
        var del = builder.RequestDispatchers[typeof(TestRequestWithResponse)];
        var func = (Func<IRequest<int>, IDependencyResolver, CancellationToken, ValueTask<Result<int>>>)del;
        var handler = new TestRequestWithResponseHandler();
        var resolver = new FakeDependencyResolver(
            new Dictionary<Type, object> { [typeof(IRequestHandler<TestRequestWithResponse, int>)] = handler });

        // Act (When)
        var result = await func(new TestRequestWithResponse(), resolver, TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task RegisterRequestHandler_VoidRequest_DispatcherDelegate_InvokesHandler()
    {
        // Arrange (Given)
        var builder = new DefaultDependencyInjectionBuilder();
        builder.RegisterRequestHandler<TestRequestHandler, TestRequest>();
        var del = builder.VoidRequestDispatchers[typeof(TestRequest)];
        var func = (Func<IRequest, IDependencyResolver, CancellationToken, ValueTask<Result>>)del;
        var handler = new TestRequestHandler();
        var resolver = new FakeDependencyResolver(
            new Dictionary<Type, object> { [typeof(IRequestHandler<TestRequest>)] = handler });

        // Act (When)
        var result = await func(new TestRequest(), resolver, TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task RegisterEventHandler_DispatcherDelegate_InvokesEventDispatcher()
    {
        // Arrange (Given)
        var builder = new DefaultDependencyInjectionBuilder();
        builder.RegisterEventHandler<TestEventHandler, TestEvent>();
        var dispatcher = builder.EventDispatchers[typeof(TestEvent)];
        var eventDispatcher = Substitute.For<IEventDispatcher>();
        eventDispatcher.DispatchAsync(Arg.Any<TestEvent>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Success()));

        // Act (When)
        var result = await dispatcher(new TestEvent(), eventDispatcher, TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        await eventDispatcher.Received(1).DispatchAsync(Arg.Any<TestEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterEventHandler_DispatcherDelegate_WithWrongEventType_Throws()
    {
        // Arrange (Given)
        var builder = new DefaultDependencyInjectionBuilder();
        builder.RegisterEventHandler<TestEventHandler, TestEvent>();
        var dispatcher = builder.EventDispatchers[typeof(TestEvent)];
        var eventDispatcher = Substitute.For<IEventDispatcher>();

        // Act & Assert (When & Then)
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher(new OtherTestEvent(), eventDispatcher, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task RegisterStreamRequestHandler_DispatcherDelegate_InvokesHandler()
    {
        // Arrange (Given)
        var builder = new DefaultDependencyInjectionBuilder();
        builder.RegisterStreamRequestHandler<TestStreamRequestHandler, TestStreamRequest, int>();
        var del = builder.StreamRequestDispatchers[typeof(TestStreamRequest)];
        var func =
            (Func<IStreamRequest<int>, IDependencyResolver, CancellationToken, IAsyncEnumerable<Result<int>>>)del;
        var handler = new TestStreamRequestHandler();
        var resolver = new FakeDependencyResolver(
            new Dictionary<Type, object> { [typeof(IStreamRequestHandler<TestStreamRequest, int>)] = handler });

        // Act (When)
        var results = new List<Result<int>>();
        await foreach (var item in func(new TestStreamRequest(), resolver, TestContext.Current.CancellationToken))
        {
            results.Add(item);
        }

        // Assert (Then)
        Assert.Single(results);
        Assert.True(results[0].IsSuccess);
    }

    // ── Behavior fixtures ────────────────────────────────────────────────────

    private sealed record OtherTestEvent : IEvent;

    /// <summary>Simple concrete resolver backed by a dictionary; avoids Castle proxy issues with private types.</summary>
    private sealed class FakeDependencyResolver(Dictionary<Type, object> services) : IDependencyResolver
    {
        public TService? GetService<TService>() where TService : class
            => services.GetValueOrDefault(typeof(TService)) as TService;

        public TService GetRequiredService<TService>() where TService : class
            => (TService)services[typeof(TService)];

        public IEnumerable<TService> GetServices<TService>() where TService : class
            => services.Values.OfType<TService>();
    }

    private sealed class TestNoResponseBehavior : IRequestPipelineBehavior<TestRequest>
    {
        public ValueTask<Result> HandleAsync(TestRequest request, RequestHandlerDelegate<TestRequest> next,
            CancellationToken cancellationToken = default) => next(request, cancellationToken);
    }

    private sealed class TestWithResponseBehavior : IRequestPipelineBehavior<TestRequestWithResponse, int>
    {
        public ValueTask<Result<int>> HandleAsync(TestRequestWithResponse request,
            RequestHandlerDelegate<TestRequestWithResponse, int> next,
            CancellationToken cancellationToken = default) => next(request, cancellationToken);
    }

    private sealed class TestEventBehavior : IEventPipelineBehavior<TestEvent>
    {
        public ValueTask<Result> HandleAsync(TestEvent @event, EventHandlerDelegate<TestEvent> next,
            CancellationToken cancellationToken = default) => next(@event, cancellationToken);
    }

    private sealed class TestStreamBehavior : IStreamRequestPipelineBehavior<TestStreamRequest, int>
    {
        public async IAsyncEnumerable<Result<int>> HandleAsync(TestStreamRequest request,
            StreamRequestHandlerDelegate<int> next,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var item in next()) yield return item;
        }
    }
}