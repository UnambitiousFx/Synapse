using UnambitiousFx.Functional;
using UnambitiousFx.Functional.Failures;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Authorization;

/// <summary>
///     Denies a request with an <see cref="UnauthorizedFailure" /> when the caller lacks the scope it needs: the
///     request's own <see cref="IRequiresScope.Scope" />, otherwise <see cref="SynapseAuthorizationOptions.QueryScope" />
///     for an <see cref="IQuery{TResponse}" /> and <see cref="SynapseAuthorizationOptions.CommandScope" /> for an
///     <see cref="ICommand" /> or <see cref="ICommand{TResponse}" />. A request that is neither, and does not declare a
///     scope, passes through, and so does an <see cref="IAllowAnonymous" /> request that does not declare one. Runs at
///     <see cref="AuthorizationBehaviorOrder.Scope" />.
/// </summary>
/// <remarks>
///     A null, empty or whitespace <see cref="IRequiresScope.Scope" /> throws an
///     <see cref="InvalidOperationException" /> naming the request type: such a scope is a bug, and denying everyone
///     silently would hide it.
/// </remarks>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResult"><see cref="Result" /> or <see cref="Result{TValue}" />.</typeparam>
public sealed class ScopeAuthorizationBehavior<TRequest, TResult> : IResultPipelineBehavior<TRequest, TResult>,
    IOrderedPipelineBehavior
    where TResult : IResult
{
    private readonly string? _defaultScope;
    private readonly ICurrentPrincipal _principal;
    private readonly IEnumerable<IAuthorizationDenialRecorder> _recorders;

    /// <summary>
    ///     Creates the behavior.
    /// </summary>
    /// <param name="principal">The caller.</param>
    /// <param name="options">The default query and command scopes.</param>
    /// <param name="recorders">The recorders told about a denial.</param>
    public ScopeAuthorizationBehavior(ICurrentPrincipal principal,
        SynapseAuthorizationOptions options,
        IEnumerable<IAuthorizationDenialRecorder> recorders)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(options);
        _principal = principal;
        ArgumentNullException.ThrowIfNull(recorders);
        _recorders = recorders;
        _defaultScope = AuthorizationChecks.DefaultScope<TRequest>(options);
    }

    /// <inheritdoc />
    public uint Order => AuthorizationBehaviorOrder.Scope;

    /// <inheritdoc />
    public ValueTask<TResult> HandleAsync(TRequest request,
        RequestNext<TRequest, TResult> next,
        CancellationToken cancellationToken = default)
    {
        return AuthorizationChecks.Scope(request, _principal, _defaultScope) is { } denied
            ? AuthorizationDenials.DenyAsync(_recorders, next, denied, cancellationToken)
            : next.InvokeAsync(request, cancellationToken);
    }
}
