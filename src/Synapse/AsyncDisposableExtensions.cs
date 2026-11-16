namespace UnambitiousFx.Synapse;

internal static class AsyncDisposableExtensions
{
    /// <summary>
    ///     Disposes with <c>DisposeAsync</c> and waits for it, for synchronous callers that own a scope that may
    ///     hold an async-only disposable (a synchronous <c>Dispose</c> would throw). Blocking is safe: these callers
    ///     run on a dedicated thread or a meter-listener thread, without a synchronization context.
    /// </summary>
    internal static void DisposeSynchronously(this IAsyncDisposable disposable)
    {
        var disposal = disposable.DisposeAsync();
        if (!disposal.IsCompleted)
        {
            disposal.AsTask().GetAwaiter().GetResult();
        }
        else
        {
            disposal.GetAwaiter().GetResult();
        }
    }
}
