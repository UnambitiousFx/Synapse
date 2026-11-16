namespace UnambitiousFx.Synapse;

internal static class AsyncDisposableExtensions
{
    /// <summary>
    ///     Disposes with <c>DisposeAsync</c> and waits for it, for synchronous callers that own a scope that may
    ///     hold an async-only disposable (a synchronous <c>Dispose</c> would throw). It blocks only when the disposal
    ///     genuinely goes asynchronous; a <see langword="null" /> scope is ignored.
    /// </summary>
    internal static void DisposeSynchronously(this IAsyncDisposable? disposable)
    {
        if (disposable is null)
        {
            return;
        }

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
