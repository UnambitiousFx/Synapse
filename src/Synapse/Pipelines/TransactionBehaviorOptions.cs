using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Pipelines;

/// <summary>
///     Options for <see cref="TransactionBehavior{TRequest,TResult}" />, set through
///     <see cref="ISynapseConfig.AddTransactions{TManager}" /> or
///     <see cref="ISynapseConfig.AddTransactionManager{TManager}" />.
/// </summary>
public sealed class TransactionBehaviorOptions
{
    /// <summary>
    ///     The default <see cref="Order" />: <c>0x8000_0000</c>, just past <see cref="IOrderedPipelineBehavior.Middle" />.
    /// </summary>
    public const uint DefaultOrder = 0x8000_0000;

    /// <summary>
    ///     Position of the transaction behavior in the request pipeline (see <see cref="IOrderedPipelineBehavior" />).
    ///     Defaults to <see cref="DefaultOrder" />: inside the behaviors that run at
    ///     <see cref="IOrderedPipelineBehavior.First" /> or <see cref="IOrderedPipelineBehavior.Middle" />, outside the
    ///     ones at <see cref="IOrderedPipelineBehavior.Last" />, so the transaction covers whatever sits between it
    ///     and the handler.
    /// </summary>
    public uint Order { get; set; } = DefaultOrder;
}
