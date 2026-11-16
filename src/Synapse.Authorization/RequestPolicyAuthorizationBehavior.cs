using UnambitiousFx.Functional;
using UnambitiousFx.Functional.Failures;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Authorization;

/// <summary>
///     Asks the request type's <see cref="IRequestAccessPolicy{TRequest}" /> whether the caller may send the request,
///     and denies it with an <see cref="UnauthorizedFailure" /> when not. A request type without a registered policy
///     passes through. Runs after the scope check and before the resource check
///     (<see cref="AuthorizationBehaviorOrder.RequestPolicy" />).
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResult"><see cref="Result" /> or <see cref="Result{TValue}" />.</typeparam>
public sealed class RequestPolicyAuthorizationBehavior<TRequest, TResult> : IResultPipelineBehavior<TRequest, TResult>,
    IOrderedPipelineBehavior
    where TResult : IResult
{
    private readonly Func<AuthorizationDenial, Failure?>? _denialFailure;
    private readonly IRequestAccessPolicy<TRequest>? _policy;
    private readonly ICurrentPrincipal _principal;
    private readonly IEnumerable<IAuthorizationDenialRecorder> _recorders;

    /// <summary>
    ///     Creates the behavior.
    /// </summary>
    /// <param name="principal">The caller.</param>
    /// <param name="recorders">The recorders told about a denial.</param>
    /// <param name="policy">The request type's policy, <see langword="null" /> when none is registered.</param>
    /// <param name="options">The <see cref="SynapseAuthorizationOptions.DenialFailure" /> to apply, if any.</param>
    public RequestPolicyAuthorizationBehavior(ICurrentPrincipal principal,
        IEnumerable<IAuthorizationDenialRecorder> recorders,
        IRequestAccessPolicy<TRequest>? policy = null,
        SynapseAuthorizationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(principal);
        _principal = principal;
        ArgumentNullException.ThrowIfNull(recorders);
        _recorders = recorders;
        _policy = policy;
        _denialFailure = options?.DenialFailure;
    }

    /// <inheritdoc />
    public uint Order => AuthorizationBehaviorOrder.RequestPolicy;

    /// <inheritdoc />
    public ValueTask<TResult> HandleAsync(TRequest request,
        RequestNext<TRequest, TResult> next,
        CancellationToken cancellationToken = default)
    {
        return _policy is null
            ? next.InvokeAsync(request, cancellationToken)
            : AuthorizeAsync(request, _policy, next, cancellationToken);
    }

    private async ValueTask<TResult> AuthorizeAsync(TRequest request,
        IRequestAccessPolicy<TRequest> policy,
        RequestNext<TRequest, TResult> next,
        CancellationToken cancellationToken)
    {
        return await AuthorizationChecks.RequestAsync(request, _principal, policy, cancellationToken) is { } denied
            ? await AuthorizationDenials.DenyAsync(_recorders, next, denied, _denialFailure, cancellationToken)
            : await next.InvokeAsync(request, cancellationToken);
    }
}
