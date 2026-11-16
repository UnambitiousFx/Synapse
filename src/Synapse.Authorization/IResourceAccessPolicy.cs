namespace UnambitiousFx.Synapse.Authorization;

/// <summary>
///     Decides who may access which resource. The application implements it and registers it with
///     <see cref="SynapseAuthorizationOptions.Policy{TPolicy}" />. Without a policy, every
///     <see cref="IResourceScoped" /> request is denied.
/// </summary>
public interface IResourceAccessPolicy
{
    /// <summary>
    ///     Checks whether <paramref name="principal" /> may access the resource <paramref name="request" /> targets.
    /// </summary>
    /// <param name="principal">The caller.</param>
    /// <param name="request">
    ///     The request, which carries <see cref="IResourceScoped.ResourceId" /> and <see cref="IResourceScoped.Access" />.
    ///     Pattern-match it to read more of the request.
    /// </param>
    /// <param name="cancellationToken">Token for cancelling the operation.</param>
    /// <returns><see langword="true" /> to let the request through, <see langword="false" /> to deny it.</returns>
    ValueTask<bool> IsAllowedAsync(ICurrentPrincipal principal,
        IResourceScoped request,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Decides whether <paramref name="principal" /> may access the resource <paramref name="request" /> targets,
    ///     and whether a denial may disclose that the resource exists. This is the method the authorization behaviors
    ///     call; by default it maps <see cref="IsAllowedAsync" /> to <see cref="ResourceAccess.Allowed" /> or
    ///     <see cref="ResourceAccess.Denied" />.
    /// </summary>
    /// <remarks>
    ///     Override it to return <see cref="ResourceAccess.Hidden" /> when the caller cannot even read the resource, so
    ///     the request fails with a <c>NotFoundFailure</c> (404) instead of an <c>UnauthorizedFailure</c> (403). Keep
    ///     <see cref="ResourceAccess.Denied" /> for a caller who can read it but not do what the request asks: they
    ///     already know it exists. <see cref="IsAllowedAsync" /> is then no longer called by this package.
    /// </remarks>
    /// <param name="principal">The caller.</param>
    /// <param name="request">
    ///     The request, which carries <see cref="IResourceScoped.ResourceId" /> and <see cref="IResourceScoped.Access" />.
    /// </param>
    /// <param name="cancellationToken">Token for cancelling the operation.</param>
    /// <returns>The decision.</returns>
    async ValueTask<ResourceAccess> CheckAsync(ICurrentPrincipal principal,
        IResourceScoped request,
        CancellationToken cancellationToken = default)
    {
        return await IsAllowedAsync(principal, request, cancellationToken)
            ? ResourceAccess.Allowed
            : ResourceAccess.Denied;
    }
}
