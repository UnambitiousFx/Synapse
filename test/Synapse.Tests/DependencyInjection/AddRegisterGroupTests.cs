using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Tests.DependencyInjection;

public sealed class AddRegisterGroupTests
{
    [Fact]
    public async Task GivenRegisterGroup_WhenRequestWithResponseDispatched_ThenReturnsSuccess()
    {
        // Arrange (Given)
        var services = new ServiceCollection()
            .AddSynapse(cfg =>
            {
                cfg.AddRegisterGroup(new TestRegisterGroup());
            })
            .AddLogging()
            .BuildServiceProvider();

        var invoker = services.GetRequiredService<IInvoker>();

        // Act (When)
        var result = await invoker.InvokeAsync(new TestRequest(), TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GivenRegisterGroup_WhenEventDispatched_ThenReturnsSuccess()
    {
        // Arrange (Given)
        var services = new ServiceCollection()
            .AddSynapse(cfg =>
            {
                cfg.AddRegisterGroup(new TestRegisterGroup());
            })
            .AddLogging()
            .BuildServiceProvider();

        var emitter = services.GetRequiredService<IEmitter>();

        // Act (When)
        var result = await emitter.EmitAsync(new TestEvent(), TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GivenRegisterGroup_WhenStreamRequestDispatched_ThenYieldsItems()
    {
        // Arrange (Given)
        var services = new ServiceCollection()
            .AddSynapse(cfg =>
            {
                cfg.AddRegisterGroup(new TestRegisterGroup());
            })
            .AddLogging()
            .BuildServiceProvider();

        var invoker = services.GetRequiredService<IInvoker>();

        // Act (When)
        var items = new List<Result<int>>();
        await foreach (var item in invoker.InvokeStreamAsync(new TestStreamRequest(), TestContext.Current.CancellationToken))
        {
            items.Add(item);
        }

        // Assert (Then)
        Assert.Single(items);
        Assert.True(items[0].IsSuccess);
    }

    [Fact]
    public async Task GivenRegisterGroup_WhenDerivedEventDispatchedAsBaseType_ThenUsesDispatcherDictionary()
    {
        // Arrange (Given)
        var services = new ServiceCollection()
            .AddSynapse(cfg =>
            {
                cfg.AddRegisterGroup(new PolymorphicTestRegisterGroup());
            })
            .AddLogging()
            .BuildServiceProvider();

        var emitter = services.GetRequiredService<IEmitter>();

        // Act (When) — TEvent=TestBaseEvent but runtime type is TestDerivedEvent, forcing dictionary lookup
        var result = await emitter.EmitAsync<TestBaseEvent>(new TestDerivedEvent(), TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsSuccess);
    }

    private sealed class TestRegisterGroup : IRegisterGroup
    {
        public void Register(IDependencyInjectionBuilder builder)
        {
            builder.RegisterRequestHandler<TestRequestHandler, TestRequest, int>();
            builder.RegisterEventHandler<TestEventHandler, TestEvent>();
            builder.RegisterStreamRequestHandler<TestStreamRequestHandler, TestStreamRequest, int>();
        }
    }

    private sealed class PolymorphicTestRegisterGroup : IRegisterGroup
    {
        public void Register(IDependencyInjectionBuilder builder)
        {
            builder.RegisterEventHandler<TestDerivedEventHandler, TestDerivedEvent>();
        }
    }

    private sealed record TestRequest : IRequest<int>;

    private sealed class TestRequestHandler : IRequestHandler<TestRequest, int>
    {
        public ValueTask<Result<int>> HandleAsync(TestRequest request, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Result.Success(42));
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

    private abstract class TestBaseEvent : IEvent;

    private sealed class TestDerivedEvent : TestBaseEvent;

    private sealed class TestDerivedEventHandler : IEventHandler<TestDerivedEvent>
    {
        public ValueTask<Result> HandleAsync(TestDerivedEvent @event, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Result.Success());
        }
    }
}
