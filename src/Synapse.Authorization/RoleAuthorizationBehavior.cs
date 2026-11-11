using UnambitiousFx.Functional;
using UnambitiousFx.Functional.Failures;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Authorization;

/// <summary>
///     Denies an <see cref="IRequiresRole" /> request with an <see cref="UnauthorizedFailure" /> when the caller lacks
///     <see cref="IRequiresRole.Role" />. Other requests pass through. Runs at
///     <see cref="AuthorizationBehaviorOrder.Role" />.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResult"><see cref="Result" /> or <see cref="Result{TValue}" />.</typeparam>
public sealed class RoleAuthorizationBehavior<TRequest, TResult> : IResultPipelineBehavior<TRequest, TResult>,
    IOrderedPipelineBehavior
    where TResult : IResult
{
    private readonly ICurrentPrincipal _principal;
    private readonly IAuthorizationDenialRecorder[] _recorders;

    /// <summary>
    ///     Creates the behavior.
    /// </summary>
    /// <param name="principal">The caller.</param>
    /// <param name="recorders">The recorders told about a denial.</param>
    public RoleAuthorizationBehavior(ICurrentPrincipal principal,
        IEnumerable<IAuthorizationDenialRecorder> recorders)
    {
        ArgumentNullException.ThrowIfNull(principal);
        _principal = principal;
        _recorders = AuthorizationDenials.ToArray(recorders);
    }

    /// <inheritdoc />
    public uint Order => AuthorizationBehaviorOrder.Role;

    /// <inheritdoc />
    public ValueTask<TResult> HandleAsync(TRequest request,
        RequestNext<TRequest, TResult> next,
        CancellationToken cancellationToken = default)
    {
        if (request is not IRequiresRole requiresRole || _principal.IsInRole(requiresRole.Role))
        {
            return next.InvokeAsync(request, cancellationToken);
        }

        return AuthorizationDenials.DenyAsync(_recorders, next,
            new AuthorizationDenial(requiresRole, AuthorizationDenialReason.MissingRole, _principal.Id, requiresRole.Role),
            new UnauthorizedFailure($"Requires role '{requiresRole.Role}'."), cancellationToken);
    }
}
