using UnambitiousFx.Functional;
using UnambitiousFx.Functional.Failures;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Authorization;

/// <summary>
///     Denies a request with an <see cref="UnauthorizedFailure" /> when the caller lacks the scope it needs: the
///     request's own <see cref="IRequiresScope.Scope" />, otherwise <see cref="SynapseAuthorizationOptions.QueryScope" />
///     for an <see cref="IQuery{TResponse}" /> and <see cref="SynapseAuthorizationOptions.CommandScope" /> for an
///     <see cref="ICommand" /> or <see cref="ICommand{TResponse}" />. A request that is neither, and does not declare a
///     scope, passes through. Runs at <see cref="AuthorizationBehaviorOrder.Scope" />.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResult"><see cref="Result" /> or <see cref="Result{TValue}" />.</typeparam>
public sealed class ScopeAuthorizationBehavior<TRequest, TResult> : IResultPipelineBehavior<TRequest, TResult>,
    IOrderedPipelineBehavior
    where TResult : IResult
{
    private readonly string? _defaultScope;
    private readonly ICurrentPrincipal _principal;
    private readonly IAuthorizationDenialRecorder[] _recorders;

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
        _recorders = AuthorizationDenials.ToArray(recorders);
        _defaultScope = RequestIntentOf<TRequest>.Value switch
        {
            RequestIntent.Command => options.CommandScope,
            RequestIntent.Query => options.QueryScope,
            _ => null
        };
    }

    /// <inheritdoc />
    public uint Order => AuthorizationBehaviorOrder.Scope;

    /// <inheritdoc />
    public ValueTask<TResult> HandleAsync(TRequest request,
        RequestNext<TRequest, TResult> next,
        CancellationToken cancellationToken = default)
    {
        var scope = request is IRequiresScope requiresScope ? requiresScope.Scope : _defaultScope;
        if (scope is null || _principal.HasScope(scope))
        {
            return next.InvokeAsync(request, cancellationToken);
        }

        return AuthorizationDenials.DenyAsync(_recorders, next,
            new AuthorizationDenial(request!, AuthorizationDenialReason.MissingScope, _principal.Id, scope),
            new UnauthorizedFailure($"Requires scope '{scope}'."), cancellationToken);
    }
}
