namespace UnambitiousFx.Synapse.Tests.Definitions;

/// <summary>Counts <see cref="AsyncOnlyDisposable" /> disposals; register it as a singleton.</summary>
public sealed class AsyncDisposeCounter
{
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public void Increment()
    {
        Interlocked.Increment(ref _count);
    }
}

/// <summary>
///     A scoped service that implements only <see cref="IAsyncDisposable" />: disposing the scope that created it
///     with a synchronous <c>Dispose</c> throws.
/// </summary>
public sealed class AsyncOnlyDisposable(AsyncDisposeCounter counter) : IAsyncDisposable
{
    public ValueTask DisposeAsync()
    {
        counter.Increment();
        return ValueTask.CompletedTask;
    }
}
