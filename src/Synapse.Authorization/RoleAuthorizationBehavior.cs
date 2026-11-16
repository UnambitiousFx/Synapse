using UnambitiousFx.Functional;
using UnambitiousFx.Functional.Failures;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Authorization;

/// <summary>
///     Denies an <see cref="IRequiresRole" /> request with an <see cref="UnauthorizedFailure" /> when the caller lacks
///     <see cref="IRequiresRole.Role" />. Other requests pass through. Runs at
///     <see cref="AuthorizationBehaviorOrder.Role" />.
/// </summary>
/// <remarks>
///     A null, empty or whitespace <see cref="IRequiresRole.Role" /> throws an <see cref="InvalidOperationException" />
///     naming the request type: such a role is a bug, and denying everyone silently would hide it.
/// </remarks>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResult"><see cref="Result" /> or <see cref="Result{TValue}" />.</typeparam>
public sealed class RoleAuthorizationBehavior<TRequest, TResult> : IResultPipelineBehavior<TRequest, TResult>,
    IOrderedPipelineBehavior
    where TResult : IResult
{
    private readonly Func<AuthorizationDenial, Failure?>? _denialFailure;
    private readonly ICurrentPrincipal _principal;
    private readonly IEnumerable<IAuthorizationDenialRecorder> _recorders;

    /// <summary>
    ///     Creates the behavior.
    /// </summary>
    /// <param name="principal">The caller.</param>
    /// <param name="recorders">The recorders told about a denial.</param>
    /// <param name="options">The <see cref="SynapseAuthorizationOptions.DenialFailure" /> to apply, if any.</param>
    public RoleAuthorizationBehavior(ICurrentPrincipal principal,
        IEnumerable<IAuthorizationDenialRecorder> recorders,
        SynapseAuthorizationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(principal);
        _principal = principal;
        ArgumentNullException.ThrowIfNull(recorders);
        _recorders = recorders;
        _denialFailure = options?.DenialFailure;
    }

    /// <inheritdoc />
    public uint Order => AuthorizationBehaviorOrder.Role;

    /// <inheritdoc />
    public ValueTask<TResult> HandleAsync(TRequest request,
        RequestNext<TRequest, TResult> next,
        CancellationToken cancellationToken = default)
    {
        return AuthorizationChecks.Role(request, _principal) is { } denied
            ? AuthorizationDenials.DenyAsync(_recorders, next, denied, _denialFailure, cancellationToken)
            : next.InvokeAsync(request, cancellationToken);
    }
}
