using System.Runtime.CompilerServices;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Tests.Streaming;

public sealed class ProxyStreamRequestHandlerTests
{
    [Fact]
    public async Task HandleAsync_WithoutBehaviors_YieldsHandlerItems()
    {
        // Arrange (Given)
        var handler = new TestStreamHandler();
        var proxy = new ProxyStreamRequestHandler<TestStreamHandler, StreamRequest, int>(
            handler,
            Array.Empty<IStreamRequestPipelineBehavior>());

        // Act (When)
        var results = await ToValuesAsync(proxy.HandleAsync(new StreamRequest(3), CancellationToken.None));

        // Assert (Then)
        Assert.Equal([1, 2, 3], results);
    }

    [Fact]
    public async Task HandleAsync_WithBehaviorChain_ExecutesBehaviorsInOrder()
    {
        // Arrange (Given)
        var trace = new List<string>();
        var handler = new TestStreamHandler(trace);
        var behaviors = new IStreamRequestPipelineBehavior[]
        {
            new TraceBehavior("b1", trace),
            new TraceBehavior("b2", trace)
        };

        var proxy = new ProxyStreamRequestHandler<TestStreamHandler, StreamRequest, int>(handler, behaviors);

        // Act (When)
        var results = await ToValuesAsync(proxy.HandleAsync(new StreamRequest(2), CancellationToken.None));

        // Assert (Then)
        Assert.Equal([1, 2], results);
        Assert.Equal([
            "b1:before",
            "b2:before",
            "handler:start",
            "b2:after",
            "b1:after"
        ], trace);
    }

    private static async Task<List<int>> ToValuesAsync(IAsyncEnumerable<Result<int>> source)
    {
        var values = new List<int>();
        await foreach (var item in source)
        {
            values.Add(item.Match(v => v, _ => -1));
        }

        return values;
    }

    private sealed record StreamRequest(int Count) : IStreamRequest<int>;

    private sealed class TestStreamHandler(List<string>? trace = null) : IStreamRequestHandler<StreamRequest, int>
    {
        public async IAsyncEnumerable<Result<int>> HandleAsync(StreamRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            trace?.Add("handler:start");
            for (var i = 1; i <= request.Count; i++)
            {
                yield return Result.Success(i);
            }

            await Task.CompletedTask;
        }
    }

    private sealed class TraceBehavior(string name, List<string> trace) : IStreamRequestPipelineBehavior
    {
        public async IAsyncEnumerable<Result<TItem>> HandleAsync<TRequest, TItem>(TRequest request,
            StreamRequestHandlerDelegate<TItem> next,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
            where TRequest : IStreamRequest<TItem>
            where TItem : notnull
        {
            trace.Add($"{name}:before");
            await foreach (var item in next())
            {
                yield return item;
            }

            trace.Add($"{name}:after");
        }
    }
}
