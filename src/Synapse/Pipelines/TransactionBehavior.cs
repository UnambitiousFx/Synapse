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
///         The <typeparamref name="TRequest" /> constraint is what keeps it off every other request: the dependency
///         injection container skips the open-generic registration for a request that is not
///         <see cref="ITransactional" />, and the source generator does not close it over one. Wire it with
///         <see cref="ISynapseConfig.AddTransactions{TManager}" />, or for Native AOT with
///         <c>[assembly: SynapseGlobalBehavior(typeof(TransactionBehavior&lt;,&gt;))]</c> plus
///         <see cref="ISynapseConfig.AddTransactionManager{TManager}" />.
///     </para>
/// </remarks>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResult"><see cref="Result" /> or <see cref="Result{TValue}" />.</typeparam>
public sealed class TransactionBehavior<TRequest, TResult> : IResultPipelineBehavior<TRequest, TResult>,
    IOrderedPipelineBehavior
    where TRequest : ITransactional
    where TResult : IResult
{
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
