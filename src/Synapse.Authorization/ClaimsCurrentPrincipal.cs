using System.Security.Claims;

namespace UnambitiousFx.Synapse.Authorization;

/// <summary>
///     Which claims <see cref="ClaimsCurrentPrincipal" /> reads.
/// </summary>
public sealed class ClaimsCurrentPrincipalOptions
{
    /// <summary>
    ///     The claim types that hold scopes, each claim holding one or more space-separated scopes. Defaults to OAuth
    ///     <c>scope</c>, Microsoft Entra ID <c>scp</c>, and the long name ASP.NET Core maps <c>scp</c> to when inbound
    ///     claim mapping is on.
    /// </summary>
    public IReadOnlyList<string> ScopeClaimTypes { get; set; } =
        ["scope", "scp", "http://schemas.microsoft.com/identity/claims/scope"];

    /// <summary>
    ///     The claim types read for <see cref="ICurrentPrincipal.Id" />, the first one present winning. Defaults to
    ///     <c>sub</c>, then <see cref="ClaimTypes.NameIdentifier" />.
    /// </summary>
    public IReadOnlyList<string> IdClaimTypes { get; set; } = ["sub", ClaimTypes.NameIdentifier];

    /// <summary>
    ///     Role claim types checked in addition to <see cref="ClaimsPrincipal.IsInRole" />, which only reads each
    ///     identity's own <see cref="ClaimsIdentity.RoleClaimType" />. Empty by default; add <c>role</c> or
    ///     <c>roles</c> when the identity provider uses them and the authentication handler does not map them.
    /// </summary>
    public IReadOnlyList<string> RoleClaimTypes { get; set; } = [];
}

/// <summary>
///     An <see cref="ICurrentPrincipal" /> over a <see cref="ClaimsPrincipal" />, such as ASP.NET Core's
///     <c>HttpContext.User</c>. Scopes are split on spaces and compared case-sensitively, as OAuth 2.0 requires.
/// </summary>
/// <remarks>
///     Register it with
///     <see cref="ServiceCollectionExtensions.AddSynapseClaimsPrincipal" />, or create it directly for a background job
///     or an MCP session that has its own <see cref="ClaimsPrincipal" />.
/// </remarks>
public sealed class ClaimsCurrentPrincipal : ICurrentPrincipal
{
    private readonly ClaimsCurrentPrincipalOptions _options;
    private readonly ClaimsPrincipal? _user;

    /// <summary>
    ///     Creates the principal.
    /// </summary>
    /// <param name="user">The caller, <see langword="null" /> when there is none (anonymous).</param>
    /// <param name="options">The claim types to read; the defaults when <see langword="null" />.</param>
    public ClaimsCurrentPrincipal(ClaimsPrincipal? user, ClaimsCurrentPrincipalOptions? options = null)
    {
        _user = user;
        _options = options ?? new ClaimsCurrentPrincipalOptions();
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Read from <see cref="ClaimsPrincipal.Identity" />, not from the presence of claims: an identity without an
    ///     authentication type is anonymous whatever its claims.
    /// </remarks>
    public bool IsAuthenticated => _user?.Identity?.IsAuthenticated == true;

    /// <inheritdoc />
    public string? Id
    {
        get
        {
            if (_user is null)
            {
                return null;
            }

            foreach (var type in _options.IdClaimTypes)
            {
                if (_user.FindFirst(type) is { } claim)
                {
                    return claim.Value;
                }
            }

            return null;
        }
    }

    /// <inheritdoc />
    public bool HasScope(string scope)
    {
        if (_user is null)
        {
            return false;
        }

        foreach (var type in _options.ScopeClaimTypes)
        {
            foreach (var claim in _user.FindAll(type))
            {
                // One claim often holds several scopes ("read write"), so HasClaim("scope", "read") would miss it.
                foreach (var granted in claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (string.Equals(granted, scope, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <inheritdoc />
    public bool IsInRole(string role)
    {
        if (_user is null)
        {
            return false;
        }

        if (_user.IsInRole(role))
        {
            return true;
        }

        foreach (var type in _options.RoleClaimTypes)
        {
            if (_user.HasClaim(type, role))
            {
                return true;
            }
        }

        return false;
    }
}
