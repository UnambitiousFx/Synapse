using System.Collections.Concurrent;

namespace UnambitiousFx.Synapse.Tests.Definitions;

/// <summary>
///     Runs a body on several threads at once, released together, and rethrows whatever they threw.
/// </summary>
/// <remarks>
///     Dedicated threads rather than <see cref="Task.Run(Action)" />: a barrier of N thread-pool work items
///     deadlocks until the pool injects the Nth thread, which it does roughly one per second, so the same test
///     costs seconds instead of milliseconds. Real threads also make the overlap genuine rather than dependent on
///     how many pool threads happen to be warm.
/// </remarks>
public static class Race
{
    public static void Run(int workers,
        Action<int> body)
    {
        using var start = new Barrier(workers);
        var failures = new ConcurrentQueue<Exception>();
        var threads = new Thread[workers];

        for (var i = 0; i < workers; i++)
        {
            var index = i;
            threads[i] = new Thread(() =>
            {
                try
                {
                    start.SignalAndWait();
                    body(index);
                }
                catch (Exception exception)
                {
                    failures.Enqueue(exception);
                }
            })
            {
                IsBackground = true,
                Name = $"race-{index}"
            };

            threads[i]
                .Start();
        }

        foreach (var thread in threads)
        {
            thread.Join();
        }

        if (failures.TryDequeue(out var first))
        {
            throw first;
        }
    }
}
