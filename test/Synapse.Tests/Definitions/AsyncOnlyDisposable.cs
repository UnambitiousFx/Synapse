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

/// <summary>Holds a <see cref="GatedAsyncDisposable" /> disposal open until the test releases it.</summary>
public sealed class DisposeGate
{
    private int _completed;

    public ManualResetEventSlim Started { get; } = new();

    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool Completed => Volatile.Read(ref _completed) == 1;

    public void MarkCompleted()
    {
        Volatile.Write(ref _completed, 1);
    }
}

/// <summary>
///     An async-only disposable whose <c>DisposeAsync</c> genuinely goes asynchronous: it completes only once
///     <see cref="DisposeGate.Release" /> is set.
/// </summary>
public sealed class GatedAsyncDisposable(DisposeGate gate) : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        gate.Started.Set();
        await gate.Release.Task.ConfigureAwait(false);
        gate.MarkCompleted();
    }
}
