namespace UnambitiousFx.Synapse.Authorization;

/// <summary>
///     Decides whether a caller may send one request type, from the request's arguments: create an invoice for a
///     customer the caller may bill, open an account in a branch the caller belongs to, book in an open period. Use it
///     for checks that are neither role, scope nor access to an existing resource (<see cref="IResourceAccessPolicy" />).
///     Register it with <see cref="SynapseAuthorizationOptions.RequestPolicy{TRequest,TPolicy}" />; a request type
///     without one is not checked.
/// </summary>
/// <typeparam name="TRequest">The request type the policy authorizes.</typeparam>
public interface IRequestAccessPolicy<in TRequest>
{
    /// <summary>
    ///     Checks whether <paramref name="principal" /> may send <paramref name="request" />.
    /// </summary>
    /// <param name="principal">The caller.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Token for cancelling the operation.</param>
    /// <returns><see langword="true" /> to let the request through, <see langword="false" /> to deny it.</returns>
    ValueTask<bool> IsAllowedAsync(ICurrentPrincipal principal,
        TRequest request,
        CancellationToken cancellationToken = default);
}
