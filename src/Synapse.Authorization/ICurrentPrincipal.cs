namespace UnambitiousFx.Synapse.Authorization;

/// <summary>
///     The caller a request runs on behalf of. The application implements it, typically as a scoped service over
///     <c>HttpContext.User</c>, an MCP session or a background job's identity, and registers it in the container.
/// </summary>
public interface ICurrentPrincipal
{
    /// <summary>
    ///     Whether the caller is authenticated. Every request from an unauthenticated caller is rejected with an
    ///     <c>UnauthenticatedFailure</c>.
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    ///     The caller's identifier (user id, client id, ...), or <see langword="null" /> when anonymous. Reported in
    ///     <see cref="AuthorizationDenial.PrincipalId" />.
    /// </summary>
    string? Id { get; }

    /// <summary>
    ///     Whether the caller was granted <paramref name="scope" />.
    /// </summary>
    /// <param name="scope">The scope a request requires, such as <c>read</c> or <c>write</c>.</param>
    /// <returns><see langword="true" /> when the caller holds the scope.</returns>
    bool HasScope(string scope);

    /// <summary>
    ///     Whether the caller has <paramref name="role" />.
    /// </summary>
    /// <param name="role">The role a request requires, such as <c>admin</c>.</param>
    /// <returns><see langword="true" /> when the caller has the role.</returns>
    bool IsInRole(string role);
}
