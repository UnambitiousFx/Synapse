using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Publish.Outbox;

/// <summary>
///     Default implementation of <see cref="IOutboxCommit" /> that delegates to <see cref="IOutboxManager" />.
/// </summary>
internal sealed class OutboxCommit : IOutboxCommit
{
    private readonly IOutboxManager _outboxManager;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OutboxCommit" /> class.
    /// </summary>
    /// <param name="outboxManager">The outbox manager to process pending events.</param>
    public OutboxCommit(IOutboxManager outboxManager)
    {
        _outboxManager = outboxManager;
    }

    /// <inheritdoc />
    public ValueTask<Result> CommitAsync(CancellationToken cancellationToken = default)
    {
        return _outboxManager.ProcessPendingAsync(cancellationToken);
    }
}