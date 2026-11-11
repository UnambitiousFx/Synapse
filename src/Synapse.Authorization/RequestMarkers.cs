namespace UnambitiousFx.Synapse.Authorization;

/// <summary>
///     Marks a request that requires its own scope instead of the default one
///     (<see cref="SynapseAuthorizationOptions.QueryScope" /> for queries,
///     <see cref="SynapseAuthorizationOptions.CommandScope" /> for commands). The scope applies even when the default
///     scopes are turned off.
/// </summary>
public interface IRequiresScope
{
    /// <summary>
    ///     The scope the caller must hold, checked with <see cref="ICurrentPrincipal.HasScope" />.
    /// </summary>
    string Scope { get; }
}

/// <summary>
///     Marks a request that only callers with a given role may send.
/// </summary>
public interface IRequiresRole
{
    /// <summary>
    ///     The role the caller must have, checked with <see cref="ICurrentPrincipal.IsInRole" />.
    /// </summary>
    string Role { get; }
}

/// <summary>
///     Marks a request aimed at one resource. The registered <see cref="IResourceAccessPolicy" /> decides whether the
///     caller may access it.
/// </summary>
public interface IResourceScoped
{
    /// <summary>
    ///     The identifier of the resource the request targets.
    /// </summary>
    string ResourceId { get; }

    /// <summary>
    ///     The kind of access the request needs on the resource, such as <c>read</c> or <c>write</c>.
    /// </summary>
    string Access { get; }
}
