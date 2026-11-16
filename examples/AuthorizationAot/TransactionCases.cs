using UnambitiousFx.Functional;
using UnambitiousFx.Synapse;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.AuthorizationAot;

// Counts scopes so the smoke run can prove AddTransactions wraps each transactional request exactly once.
public sealed class CountingTransactionManager : ITransactionManager
{
    public static int Begun;
    public static int Committed;

    public ValueTask<ITransactionScope> BeginAsync(CancellationToken cancellationToken = default)
    {
        Begun++;
        return ValueTask.FromResult<ITransactionScope>(new Scope());
    }

    private sealed class Scope : ITransactionScope
    {
        public ValueTask CommitAsync(CancellationToken cancellationToken = default)
        {
            Committed++;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

public sealed record Deposit : ICommand, ITransactional;

public readonly record struct Tally : ICommand<int>, ITransactional;

[RequestHandler<Deposit>]
public sealed class DepositHandler : IRequestHandler<Deposit>
{
    public ValueTask<Result> HandleAsync(Deposit request, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Result.Success());
}

[RequestHandler<Tally, int>]
public sealed class TallyHandler : IRequestHandler<Tally, int>
{
    public ValueTask<Result<int>> HandleAsync(Tally request, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Result.Success(7));
}
