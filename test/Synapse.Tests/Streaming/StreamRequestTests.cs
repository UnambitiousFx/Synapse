using System.Runtime.CompilerServices;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Tests.Streaming;

public sealed class StreamRequestTests
{
    [Fact]
    public async Task StreamRequest_ShouldYieldAllItems()
    {
        // Arrange
        var handler = new TestStreamRequestHandler();
        var request = new TestStreamRequest { Count = 10 };
        var items = new List<int>();

        // Act
        await foreach (var result in handler.HandleAsync(request))
        {
            Assert.True(result.IsSuccess);
            items.Add(result.Match(v => v, _ => -1));
        }

        // Assert
        Assert.Equal(10, items.Count);
        Assert.Equal(Enumerable.Range(0, 10)
            .ToList(), items);
    }

    [Fact]
    public async Task StreamRequest_ShouldSupportCancellation()
    {
        // Arrange
        var handler = new TestStreamRequestHandler();
        var request = new TestStreamRequest { Count = 100 };
        var cts = new CancellationTokenSource();
        var items = new List<int>();

        // Act
        await foreach (var result in handler.HandleAsync(request, cts.Token))
        {
            items.Add(result.Match(v => v, _ => -1));

            // Cancel after 5 items
            if (items.Count == 5) await cts.CancelAsync();
        }

        // Assert
        Assert.Equal(5, items.Count);
    }

    [Fact]
    public async Task StreamRequest_WithEmptyStream_ShouldReturnNoItems()
    {
        // Arrange
        var handler = new TestStreamRequestHandler();
        var request = new TestStreamRequest { Count = 0 };
        var items = new List<int>();

        // Act
        await foreach (var result in handler.HandleAsync(request)) items.Add(result.Match(v => v, _ => -1));

        // Assert
        Assert.Empty(items);
    }

    [Fact]
    public async Task StreamRequest_ShouldHandleErrors()
    {
        // Arrange
        var request = new TestStreamRequest { Count = 10 };
        var errorCount = 0;
        var itemCount = 0;

        // Handler that produces errors on even numbers
        async IAsyncEnumerable<Result<int>> ErrorProducingHandler(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            for (var i = 0; i < request.Count; i++)
            {
                await Task.Delay(1, cancellationToken);

                if (i % 2 == 0)
                    yield return Result.Failure<int>($"Error at {i}");
                else
                    yield return Result.Success(i);
            }
        }

        // Act
        await foreach (var result in ErrorProducingHandler())
            if (result.IsSuccess)
                itemCount++;
            else
                errorCount++;

        // Assert
        Assert.Equal(5, itemCount);
        Assert.Equal(5, errorCount);
    }

    // Test request
    private sealed record TestStreamRequest : IStreamRequest<int>
    {
        public int Count { get; init; }
    }

    // Test handler
    private sealed class TestStreamRequestHandler : IStreamRequestHandler<TestStreamRequest, int>
    {
        public async IAsyncEnumerable<Result<int>> HandleAsync(TestStreamRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            for (var i = 0; i < request.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested) yield break;

                await Task.Delay(1, cancellationToken);
                yield return Result.Success(i);
            }
        }
    }
}