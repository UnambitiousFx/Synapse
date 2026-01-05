using System.Runtime.CompilerServices;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Tests.Streaming;

public sealed class StreamPipelineBehaviorTests
{
    [Fact]
    public async Task StreamPipelineBehavior_ShouldInterceptAllItems()
    {
        // Arrange
        var behavior = new CountingBehavior();
        var items = new List<int>();

        async IAsyncEnumerable<Result<int>> Handler()
        {
            for (var i = 0; i < 10; i++) yield return Result.Success(i);

            await Task.CompletedTask;
        }

        // Act
        await foreach (var result in behavior.HandleAsync(
                           new TestStreamRequest { Count = 10 },
                           Handler))
            items.Add(result.Match(v => v, _ => -1));

        // Assert
        Assert.Equal(10, behavior.ItemsProcessed);
        Assert.Equal(10, items.Count);
    }

    [Fact]
    public async Task StreamPipelineBehavior_ShouldAllowFiltering()
    {
        // Arrange
        var behavior = new FilteringBehavior();
        var items = new List<int>();

        async IAsyncEnumerable<Result<int>> Handler()
        {
            for (var i = 0; i < 10; i++)
                if (i % 2 == 0)
                    yield return Result.Failure<int>($"Error at {i}");
                else
                    yield return Result.Success(i);

            await Task.CompletedTask;
        }

        // Act
        await foreach (var result in behavior.HandleAsync(
                           new TestStreamRequest { Count = 10 },
                           Handler))
            items.Add(result.Match(v => v, _ => -1));

        // Assert
        Assert.Equal(5, items.Count); // Only successful items (odd numbers)
        Assert.Equal([1, 3, 5, 7, 9], items);
    }

    [Fact]
    public async Task StreamPipelineBehavior_ShouldChainMultipleBehaviors()
    {
        // Arrange
        var countingBehavior = new CountingBehavior();
        var filteringBehavior = new FilteringBehavior();
        var items = new List<int>();

        async IAsyncEnumerable<Result<int>> Handler()
        {
            for (var i = 0; i < 10; i++)
                if (i % 3 == 0)
                    yield return Result.Failure<int>($"Error at {i}");
                else
                    yield return Result.Success(i);

            await Task.CompletedTask;
        }

        // Act - Chain behaviors: counting -> filtering -> handler
        await foreach (var result in countingBehavior.HandleAsync(
                           new TestStreamRequest { Count = 10 },
                           () => filteringBehavior.HandleAsync(
                               new TestStreamRequest { Count = 10 },
                               Handler)))
            items.Add(result.Match(v => v, _ => -1));

        // Assert
        // Counting behavior sees filtered items (6 items: 1, 2, 4, 5, 7, 8 - all except 0, 3, 6, 9)
        Assert.Equal(6, countingBehavior.ItemsProcessed);
        Assert.Equal(6, items.Count);
        Assert.Equal([1, 2, 4, 5, 7, 8], items);
    }

    private sealed record TestStreamRequest : IStreamRequest<int>
    {
        public int Count { get; init; }
    }

    private sealed class CountingBehavior : IStreamRequestPipelineBehavior
    {
        public int ItemsProcessed { get; private set; }

        public async IAsyncEnumerable<Result<TItem>> HandleAsync<TRequest, TItem>(TRequest request,
            StreamRequestHandlerDelegate<TItem> next,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
            where TRequest : IStreamRequest<TItem>
            where TItem : notnull
        {
            await foreach (var item in next())
            {
                ItemsProcessed++;
                yield return item;
            }
        }
    }

    private sealed class FilteringBehavior : IStreamRequestPipelineBehavior
    {
        public async IAsyncEnumerable<Result<TItem>> HandleAsync<TRequest, TItem>(TRequest request,
            StreamRequestHandlerDelegate<TItem> next,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
            where TRequest : IStreamRequest<TItem>
            where TItem : notnull
        {
            await foreach (var item in next())
                // Only yield successful items
                if (item.IsSuccess)
                    yield return item;
        }
    }
}