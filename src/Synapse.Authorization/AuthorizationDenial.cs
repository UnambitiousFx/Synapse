namespace UnambitiousFx.Synapse.Authorization;

/// <summary>
///     Called every time an authorization behavior denies a request, before the failure is returned. Use it to audit,
///     log or count denials. Register one or more with
///     <see cref="SynapseAuthorizationOptions.DenialRecorder{TRecorder}" />.
/// </summary>
public interface IAuthorizationDenialRecorder
{
    /// <summary>
    ///     Records a denial.
    /// </summary>
    /// <param name="denial">What was denied and why.</param>
    /// <param name="cancellationToken">Token for cancelling the operation.</param>
    /// <returns>A task that completes once the denial is recorded.</returns>
    ValueTask RecordAsync(AuthorizationDenial denial, CancellationToken cancellationToken = default);
}

/// <summary>
///     A denied request, as passed to <see cref="IAuthorizationDenialRecorder" />.
/// </summary>
/// <param name="Request">The denied request.</param>
/// <param name="Reason">Which check denied it.</param>
/// <param name="PrincipalId">The caller's <see cref="ICurrentPrincipal.Id" />, <see langword="null" /> when anonymous.</param>
/// <param name="Requirement">
///     What the caller lacked: the role, the scope or the resource access. <see langword="null" /> for
///     <see cref="AuthorizationDenialReason.Unauthenticated" />.
/// </param>
/// <param name="ResourceId">
///     The targeted resource for <see cref="AuthorizationDenialReason.ResourceAccessDenied" />, otherwise
///     <see langword="null" />.
/// </param>
public sealed record AuthorizationDenial(
    object Request,
    AuthorizationDenialReason Reason,
    string? PrincipalId,
    string? Requirement = null,
    string? ResourceId = null);

/// <summary>
///     Which authorization check denied a request.
/// </summary>
public enum AuthorizationDenialReason
{
    /// <summary>The caller is not authenticated.</summary>
    Unauthenticated,

    /// <summary>The caller lacks the role an <see cref="IRequiresRole" /> request needs.</summary>
    MissingRole,

    /// <summary>The caller lacks the scope the request needs.</summary>
    MissingScope,

    /// <summary>
    ///     The <see cref="IResourceAccessPolicy" /> denied access to an <see cref="IResourceScoped" /> request's resource,
    ///     or no policy is registered.
    /// </summary>
    ResourceAccessDenied
}
