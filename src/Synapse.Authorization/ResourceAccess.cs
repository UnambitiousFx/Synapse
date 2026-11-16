namespace UnambitiousFx.Synapse.Authorization;

/// <summary>
///     What an <see cref="IResourceAccessPolicy" /> decides for a resource-scoped request.
/// </summary>
/// <remarks>
///     <see cref="Denied" /> is the default value, so an uninitialized decision (or a mocked policy left unconfigured)
///     fails closed.
/// </remarks>
public enum ResourceAccess
{
    /// <summary>
    ///     The caller may see the resource but not do what the request asks: an <c>UnauthorizedFailure</c> (403).
    /// </summary>
    Denied = 0,

    /// <summary>The request goes on.</summary>
    Allowed = 1,

    /// <summary>
    ///     The caller may not even see the resource: a <c>NotFoundFailure</c> (404), as if it did not exist, so probing
    ///     identifiers tells nothing.
    /// </summary>
    Hidden = 2
}
