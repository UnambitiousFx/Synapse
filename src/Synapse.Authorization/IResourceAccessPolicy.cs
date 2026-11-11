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
}
