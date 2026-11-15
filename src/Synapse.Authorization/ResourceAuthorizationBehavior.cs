using UnambitiousFx.Functional;
using UnambitiousFx.Functional.Failures;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Authorization;

/// <summary>
///     Asks the <see cref="IResourceAccessPolicy" /> whether the caller may access the resource an
///     <see cref="IResourceScoped" /> request targets, and denies it with an <see cref="UnauthorizedFailure" /> when not.
///     Without a registered policy, every <see cref="IResourceScoped" /> request is denied. Other requests pass through.
///     Runs last of the authorization behaviors (<see cref="AuthorizationBehaviorOrder.Resource" />).
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResult"><see cref="Result" /> or <see cref="Result{TValue}" />.</typeparam>
public sealed class ResourceAuthorizationBehavior<TRequest, TResult> : IResultPipelineBehavior<TRequest, TResult>,
    IOrderedPipelineBehavior
    where TResult : IResult
{
    private readonly IResourceAccessPolicy? _policy;
    private readonly ICurrentPrincipal _principal;
    private readonly IEnumerable<IAuthorizationDenialRecorder> _recorders;

    /// <summary>
    ///     Creates the behavior.
    /// </summary>
    /// <param name="principal">The caller.</param>
    /// <param name="recorders">The recorders told about a denial.</param>
    /// <param name="policy">The application's policy, <see langword="null" /> when none is registered.</param>
    public ResourceAuthorizationBehavior(ICurrentPrincipal principal,
        IEnumerable<IAuthorizationDenialRecorder> recorders,
        IResourceAccessPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(principal);
        _principal = principal;
        ArgumentNullException.ThrowIfNull(recorders);
        _recorders = recorders;
        _policy = policy;
    }

    /// <inheritdoc />
    public uint Order => AuthorizationBehaviorOrder.Resource;

    /// <inheritdoc />
    public ValueTask<TResult> HandleAsync(TRequest request,
        RequestNext<TRequest, TResult> next,
        CancellationToken cancellationToken = default)
    {
        return request is IResourceScoped scoped
            ? AuthorizeAsync(request, scoped, next, cancellationToken)
            : next.InvokeAsync(request, cancellationToken);
    }

    private async ValueTask<TResult> AuthorizeAsync(TRequest request,
        IResourceScoped scoped,
        RequestNext<TRequest, TResult> next,
        CancellationToken cancellationToken)
    {
        return await AuthorizationChecks.ResourceAsync(scoped, _principal, _policy, cancellationToken) is { } denied
            ? await AuthorizationDenials.DenyAsync(_recorders, next, denied, cancellationToken)
            : await next.InvokeAsync(request, cancellationToken);
    }
}
