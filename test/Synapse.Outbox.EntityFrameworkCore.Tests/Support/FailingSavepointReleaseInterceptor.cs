using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace UnambitiousFx.Synapse.Outbox.EntityFrameworkCore.Tests.Support;

/// <summary>
///     Makes the next savepoint release throw once armed. Armed by hand, because EF Core's own
///     <c>SaveChanges</c> also creates and releases savepoints inside a transaction.
/// </summary>
public sealed class FailingSavepointReleaseInterceptor : DbTransactionInterceptor
{
    public bool FailNextRelease { get; set; }

    public override ValueTask<InterceptionResult> ReleasingSavepointAsync(DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        if (FailNextRelease)
        {
            FailNextRelease = false;
            throw new InvalidOperationException("Simulated savepoint release failure.");
        }

        return base.ReleasingSavepointAsync(transaction, eventData, result, cancellationToken);
    }
}
