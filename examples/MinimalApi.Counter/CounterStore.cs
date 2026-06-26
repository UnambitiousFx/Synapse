namespace UnambitiousFx.Examples.MinimalApi.Counter;

/// <summary>
///     Thread-safe in-memory counter backing the Counter feature. Registered as a singleton so state persists
///     across requests within a host instance.
/// </summary>
public sealed class CounterStore
{
    private int _value;

    /// <summary>Current counter value.</summary>
    public int Current => Volatile.Read(ref _value);

    /// <summary>Atomically increments the counter and returns the new value.</summary>
    public int Increment() => Interlocked.Increment(ref _value);
}
