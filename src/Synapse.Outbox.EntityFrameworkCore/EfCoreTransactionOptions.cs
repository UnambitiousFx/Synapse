using Microsoft.EntityFrameworkCore;

namespace UnambitiousFx.Synapse.Outbox.EntityFrameworkCore;

/// <summary>
///     Options for <see cref="EfCoreTransactionManager" />, set through
///     <see cref="ServiceCollectionExtensions.AddEfCoreTransactions" />.
/// </summary>
public sealed class EfCoreTransactionOptions
{
    private readonly List<Type> _contextTypes = [];

    /// <summary>
    ///     The <see cref="DbContext" /> types enlisted in every transaction, in the order they were added. The
    ///     first one opens the transaction; the others join it.
    /// </summary>
    public IReadOnlyList<Type> ContextTypes => _contextTypes;

    /// <summary>
    ///     Whether committing the outermost transaction then dispatches the pending outbox events through
    ///     <c>IOutboxCommit.CommitAsync</c>. Defaults to <see langword="true" />. Turn it off when the background
    ///     outbox dispatcher delivers the events, to save the extra claim query per commit.
    /// </summary>
    public bool FlushOutboxOnCommit { get; set; } = true;

    /// <summary>
    ///     Enlists <typeparamref name="TContext" /> in every transaction the manager opens. Enlist the context that
    ///     owns the outbox table too, so the stored events commit or roll back with the business data.
    /// </summary>
    /// <remarks>
    ///     Every enlisted context must use the same <see cref="System.Data.Common.DbConnection" /> instance in a
    ///     given scope, for example a Scoped connection passed to <c>UseNpgsql(connection)</c>: a
    ///     <c>DbTransaction</c> can only be shared by contexts on the connection it was opened on. Adding the same
    ///     type twice has no effect.
    /// </remarks>
    /// <typeparam name="TContext">The context type, registered in the container (e.g. with <c>AddDbContext</c>).</typeparam>
    /// <returns>The same options, for chaining.</returns>
    public EfCoreTransactionOptions Enlist<TContext>()
        where TContext : DbContext
    {
        if (!_contextTypes.Contains(typeof(TContext)))
        {
            _contextTypes.Add(typeof(TContext));
        }

        return this;
    }
}
