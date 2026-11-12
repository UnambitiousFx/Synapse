namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Opens transaction scopes for <see cref="ITransactional" /> requests.
/// </summary>
/// <remarks>
///     An implementation is registered Scoped, so every request handled in one dependency injection scope shares
///     it. When a scope is already open, <see cref="BeginAsync" /> opens a nested one (a savepoint, for a database)
///     instead of a second transaction, so a transactional command that invokes another one composes: the inner
///     command can roll back on its own, and nothing is durable until the outermost scope commits.
/// </remarks>
public interface ITransactionManager
{
    /// <summary>
    ///     Opens a transaction, or a nested scope inside the transaction that is already open.
    /// </summary>
    /// <param name="cancellationToken">A token to observe while opening the scope.</param>
    /// <returns>
    ///     The opened scope. Commit it with <see cref="ITransactionScope.CommitAsync" />; disposing it without a
    ///     commit rolls it back.
    /// </returns>
    ValueTask<ITransactionScope> BeginAsync(CancellationToken cancellationToken = default);
}
