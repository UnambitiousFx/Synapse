using UnambitiousFx.Functional;
using UnambitiousFx.Functional.Failures;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Authorization;

/// <summary>
///     Rejects every request from an unauthenticated caller with an <see cref="UnauthenticatedFailure" />. Runs first
///     of the authorization behaviors (<see cref="AuthorizationBehaviorOrder.Actor" />), so the role, scope and
///     resource checks only ever see authenticated callers.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResult"><see cref="Result" /> or <see cref="Result{TValue}" />.</typeparam>
public sealed class ActorAuthorizationBehavior<TRequest, TResult> : IResultPipelineBehavior<TRequest, TResult>,
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
    public ActorAuthorizationBehavior(ICurrentPrincipal principal,
        IEnumerable<IAuthorizationDenialRecorder> recorders)
    {
        ArgumentNullException.ThrowIfNull(principal);
        _principal = principal;
        _recorders = AuthorizationDenials.ToArray(recorders);
    }

    /// <inheritdoc />
    public uint Order => AuthorizationBehaviorOrder.Actor;

    /// <inheritdoc />
    public ValueTask<TResult> HandleAsync(TRequest request,
        RequestNext<TRequest, TResult> next,
        CancellationToken cancellationToken = default)
    {
        if (_principal.IsAuthenticated)
        {
            return next.InvokeAsync(request, cancellationToken);
        }

        return AuthorizationDenials.DenyAsync(_recorders, next,
            new AuthorizationDenial(request!, AuthorizationDenialReason.Unauthenticated, _principal.Id),
            new UnauthenticatedFailure("Authentication is required."), cancellationToken);
    }
}
