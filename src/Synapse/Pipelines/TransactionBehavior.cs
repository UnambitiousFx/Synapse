using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Pipelines;

/// <summary>
///     Runs an <see cref="ITransactional" /> request inside an <see cref="ITransactionScope" />: the scope commits
///     when the rest of the pipeline returns a successful <see cref="Result" /> or <see cref="Result{TValue}" />,
///     and rolls back when it returns a failure or throws.
/// </summary>
/// <remarks>
///     <para>
///         A failed <c>Result</c> is not an exception, so a try/catch or <c>TransactionScope</c> wrapper would
///         commit it. This behavior reads <see cref="IResult.IsSuccess" /> instead.
///     </para>
///     <para>
///         It has no <c>ITransactional</c> constraint, so the source generator can close it over every request
///         (Native-AOT safe); a request that is not <see cref="ITransactional" /> passes straight through to the next
///         stage without calling the <see cref="ITransactionManager" /> (the constructor still resolves it). Wire it with
///         <see cref="ISynapseConfig.AddTransactions{TManager}" />, which registers it only for
///         <see cref="ITransactional" /> requests, or with
///         <c>[assembly: SynapseGlobalBehavior(typeof(TransactionBehavior&lt;,&gt;))]</c> plus
///         <see cref="ISynapseConfig.AddTransactionManager{TManager}" />.
///     </para>
/// </remarks>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResult"><see cref="Result" /> or <see cref="Result{TValue}" />.</typeparam>
public sealed class TransactionBehavior<TRequest, TResult> : IResultPipelineBehavior<TRequest, TResult>,
    IOrderedPipelineBehavior
    where TResult : IResult
{
    private static readonly bool IsTransactional = typeof(ITransactional).IsAssignableFrom(typeof(TRequest));

    private readonly ITransactionManager _transactionManager;

    /// <summary>
    ///     Creates the behavior.
    /// </summary>
    /// <param name="transactionManager">Opens the scope the request runs in.</param>
    /// <param name="options">The behavior options.</param>
    public TransactionBehavior(ITransactionManager transactionManager, TransactionBehaviorOptions options)
    {
        _transactionManager = transactionManager;
        Order = options.Order;
    }

    /// <inheritdoc />
    public uint Order { get; }

    /// <inheritdoc />
    public async ValueTask<TResult> HandleAsync(TRequest request,
        RequestNext<TRequest, TResult> next,
        CancellationToken cancellationToken = default)
    {
        if (!IsTransactional)
        {
            return await next.InvokeAsync(request, cancellationToken);
        }

        // Disposing without a commit is the rollback, so an exception thrown by the handler rolls back too.
        await using var scope = await _transactionManager.BeginAsync(cancellationToken);
        var result = await next.InvokeAsync(request, cancellationToken);
        if (result.IsSuccess)
        {
            await scope.CommitAsync(cancellationToken);
        }

        return result;
    }
}
