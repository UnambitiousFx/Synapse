using UnambitiousFx.Functional;
using UnambitiousFx.Functional.Failures;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Authorization;

/// <summary>
///     Asks the <see cref="IResourceAccessPolicy" /> whether the caller may access the resource an
///     <see cref="IResourceScoped" /> request targets, and denies it with an <see cref="UnauthorizedFailure" /> when not.
///     Without a registered policy, every <see cref="IResourceScoped" /> request is denied. Other requests pass through,
///     and so does an <see cref="IOptionallyResourceScoped" /> request whose
///     <see cref="IOptionallyResourceScoped.OptionalResourceId" /> is <see langword="null" />.
///     Runs last of the authorization behaviors (<see cref="AuthorizationBehaviorOrder.Resource" />).
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResult"><see cref="Result" /> or <see cref="Result{TValue}" />.</typeparam>
public sealed class ResourceAuthorizationBehavior<TRequest, TResult> : IResultPipelineBehavior<TRequest, TResult>,
    IOrderedPipelineBehavior
    where TResult : IResult
{
    private readonly Func<AuthorizationDenial, Failure?>? _denialFailure;
    private readonly IResourceAccessPolicy? _policy;
    private readonly ICurrentPrincipal _principal;
    private readonly IEnumerable<IAuthorizationDenialRecorder> _recorders;
    private readonly SystemBypass _systemBypass;

    /// <summary>
    ///     Creates the behavior.
    /// </summary>
    /// <param name="principal">The caller.</param>
    /// <param name="recorders">The recorders told about a denial.</param>
    /// <param name="policy">The application's policy, <see langword="null" /> when none is registered.</param>
    /// <param name="options">The <see cref="SynapseAuthorizationOptions.DenialFailure" /> to apply, if any.</param>
    public ResourceAuthorizationBehavior(ICurrentPrincipal principal,
        IEnumerable<IAuthorizationDenialRecorder> recorders,
        IResourceAccessPolicy? policy = null,
        SynapseAuthorizationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(principal);
        _principal = principal;
        ArgumentNullException.ThrowIfNull(recorders);
        _recorders = recorders;
        _policy = policy;
        _denialFailure = options?.DenialFailure;
        _systemBypass = options?.SystemBypass ?? SystemBypass.All;
    }

    /// <inheritdoc />
    public uint Order => AuthorizationBehaviorOrder.Resource;

    /// <inheritdoc />
    public ValueTask<TResult> HandleAsync(TRequest request,
        RequestNext<TRequest, TResult> next,
        CancellationToken cancellationToken = default)
    {
        return request is IResourceScoped scoped && !AuthorizationChecks.SkipsResourceChecks(_principal, _systemBypass)
            ? AuthorizeAsync(request, scoped, next, cancellationToken)
            : next.InvokeAsync(request, cancellationToken);
    }

    private async ValueTask<TResult> AuthorizeAsync(TRequest request,
        IResourceScoped scoped,
        RequestNext<TRequest, TResult> next,
        CancellationToken cancellationToken)
    {
        return await AuthorizationChecks.ResourceAsync<TRequest>(scoped, _principal, _policy, cancellationToken) is { } denied
            ? await AuthorizationDenials.DenyAsync(_recorders, next, denied, _denialFailure, cancellationToken)
            : await next.InvokeAsync(request, cancellationToken);
    }
}
