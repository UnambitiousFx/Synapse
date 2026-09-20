using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Pipelines;

/// <summary>
///     Pipeline behavior that discards the outbox events a request stored when it fails or throws, so they are not
///     dispatched by a later, unrelated <see cref="IOutboxCommit.CommitAsync" />. This variant handles requests that
///     do not produce a response.
/// </summary>
/// <remarks>
///     Meant for the in-memory outbox storage, which is not enlisted in the database transaction. It is opt-in: wire
///     it with <c>[assembly: SynapseGlobalBehavior(typeof(OutboxDiscardOnFailureBehavior&lt;&gt;))]</c> or
///     <see cref="ISynapseConfig.RegisterOutboxDiscardOnFailure{TRequest}" />.
/// </remarks>
/// <typeparam name="TRequest">The request type.</typeparam>
public sealed class OutboxDiscardOnFailureBehavior<TRequest> : IRequestPipelineBehavior<TRequest>,
    IOrderedPipelineBehavior
    where TRequest : IRequest
{
    private readonly IOutboxDiscard _outboxDiscard;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OutboxDiscardOnFailureBehavior{TRequest}" /> class.
    /// </summary>
    public OutboxDiscardOnFailureBehavior(IOutboxDiscard outboxDiscard)
    {
        _outboxDiscard = outboxDiscard;
    }

    /// <summary>
    ///     Runs outermost so a failure raised by any other behavior in the chain is seen too.
    /// </summary>
    public uint Order => IOrderedPipelineBehavior.First;

    /// <inheritdoc />
    public async ValueTask<Result> HandleAsync(TRequest request,
        RequestHandlerDelegate<TRequest> next,
        CancellationToken cancellationToken = default)
    {
        Result response;
        try
        {
            response = await next(request, cancellationToken);
        }
        catch
        {
            // The scope's events are taken back even when the token is already canceled: nothing else will.
            await _outboxDiscard.DiscardStoredAsync(CancellationToken.None);
            throw;
        }

        if (response.IsFailure)
        {
            await _outboxDiscard.DiscardStoredAsync(cancellationToken);
        }

        return response;
    }
}

/// <summary>
///     Pipeline behavior that discards the outbox events a request stored when it fails or throws, so they are not
///     dispatched by a later, unrelated <see cref="IOutboxCommit.CommitAsync" />. This variant handles requests that
///     produce a response.
/// </summary>
/// <remarks>
///     See <see cref="OutboxDiscardOnFailureBehavior{TRequest}" /> for when to use it and how to wire it.
/// </remarks>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public sealed class OutboxDiscardOnFailureBehavior<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>,
    IOrderedPipelineBehavior
    where TRequest : IRequest<TResponse>
    where TResponse : notnull
{
    private readonly IOutboxDiscard _outboxDiscard;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OutboxDiscardOnFailureBehavior{TRequest,TResponse}" /> class.
    /// </summary>
    public OutboxDiscardOnFailureBehavior(IOutboxDiscard outboxDiscard)
    {
        _outboxDiscard = outboxDiscard;
    }

    /// <summary>
    ///     Runs outermost so a failure raised by any other behavior in the chain is seen too.
    /// </summary>
    public uint Order => IOrderedPipelineBehavior.First;

    /// <inheritdoc />
    public async ValueTask<Result<TResponse>> HandleAsync(TRequest request,
        RequestHandlerDelegate<TRequest, TResponse> next,
        CancellationToken cancellationToken = default)
    {
        Result<TResponse> response;
        try
        {
            response = await next(request, cancellationToken);
        }
        catch
        {
            // The scope's events are taken back even when the token is already canceled: nothing else will.
            await _outboxDiscard.DiscardStoredAsync(CancellationToken.None);
            throw;
        }

        if (response.IsFailure)
        {
            await _outboxDiscard.DiscardStoredAsync(cancellationToken);
        }

        return response;
    }
}
