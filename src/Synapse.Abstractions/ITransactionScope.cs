namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     A transaction, or a nested scope inside one, opened by <see cref="ITransactionManager.BeginAsync" />.
/// </summary>
/// <remarks>
///     Disposing the scope without calling <see cref="CommitAsync" /> rolls it back, so an <c>await using</c> block
///     rolls back on every path that does not reach the commit: an early return, a failure, an exception. Scopes
///     nest and must be completed innermost first.
/// </remarks>
public interface ITransactionScope : IAsyncDisposable
{
    /// <summary>
    ///     Commits the scope. For a nested scope, the work becomes part of the enclosing scope and is durable only
    ///     once the outermost scope commits.
    /// </summary>
    /// <param name="cancellationToken">A token to observe while committing.</param>
    /// <returns>A task that completes once the scope is committed.</returns>
    ValueTask CommitAsync(CancellationToken cancellationToken = default);
}
