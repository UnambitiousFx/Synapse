using System.Data.Common;

namespace UnambitiousFx.Synapse.Outbox.AdoNet;

/// <summary>
///     Holds the database transaction that outbox writes in the current scope enlist in.
/// </summary>
/// <remarks>
///     Registered Scoped by <see cref="ServiceCollectionExtensions.AddAdoNetEventOutbox" />. Hand it the transaction
///     your business writes run in, before emitting with <c>EmitMode.Outbox</c>, and the stored entry commits or
///     rolls back together with them. Without one, each stored entry is written on its own connection and
///     committed immediately.
/// </remarks>
public sealed class AdoNetOutboxTransaction
{
    /// <summary>
    ///     The transaction outbox writes enlist in, or <see langword="null" /> when none was set.
    /// </summary>
    public DbTransaction? Current { get; private set; }

    /// <summary>
    ///     Makes outbox writes in this scope run on <paramref name="transaction" />'s connection, inside it.
    /// </summary>
    /// <param name="transaction">An open transaction on the database that holds the outbox table.</param>
    /// <exception cref="ArgumentException">The transaction has no connection, so it has already completed.</exception>
    public void Enlist(DbTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        if (transaction.Connection is null)
        {
            throw new ArgumentException("The transaction has already been committed or rolled back.",
                nameof(transaction));
        }

        Current = transaction;
    }

    /// <summary>
    ///     Stops enlisting outbox writes in the transaction set with <see cref="Enlist" />.
    /// </summary>
    public void Clear()
    {
        Current = null;
    }
}
