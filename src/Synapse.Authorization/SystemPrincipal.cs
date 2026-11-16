namespace UnambitiousFx.Synapse.Authorization;

/// <summary>
///     The technical identity background work runs as: nightly jobs, schedulers, migrations, outbox handlers. It is
///     authenticated, holds every scope and role, and is <see cref="ICurrentPrincipal.IsSystem" />, so the
///     authorization behaviors let it through as <see cref="SynapseAuthorizationOptions.SystemBypass" /> says, without
///     calling the policies. Register it as the job scope's <see cref="ICurrentPrincipal" />.
/// </summary>
/// <remarks>
///     For a system identity that should keep some checks, with only some scopes or roles, implement
///     <see cref="ICurrentPrincipal" /> with <see cref="ICurrentPrincipal.IsSystem" /> returning <see langword="true" />
///     and set <see cref="SynapseAuthorizationOptions.SystemBypass" /> to <see cref="SystemBypass.ResourceChecks" />.
/// </remarks>
public sealed class SystemPrincipal : ICurrentPrincipal
{
    private SystemPrincipal()
    {
    }

    /// <summary>The system principal.</summary>
    public static SystemPrincipal Instance { get; } = new();

    /// <inheritdoc />
    public bool IsAuthenticated => true;

    /// <inheritdoc />
    /// <remarks>Always <c>system</c>.</remarks>
    public string Id => "system";

    /// <inheritdoc />
    public bool IsSystem => true;

    /// <inheritdoc />
    public bool HasScope(string scope)
    {
        return true;
    }

    /// <inheritdoc />
    public bool IsInRole(string role)
    {
        return true;
    }
}

/// <summary>
///     Which authorization checks a <see cref="ICurrentPrincipal.IsSystem" /> principal skips. A skipped check calls
///     no policy and records no denial.
/// </summary>
public enum SystemBypass
{
    /// <summary>Every check: actor, role, scope, request policy and resource.</summary>
    All,

    /// <summary>
    ///     The request policy and resource checks only. The actor, role and scope checks run against the principal as
    ///     for any caller.
    /// </summary>
    ResourceChecks,

    /// <summary>None: the system principal is checked like any caller.</summary>
    None
}
