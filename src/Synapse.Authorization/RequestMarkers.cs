namespace UnambitiousFx.Synapse.Authorization;

/// <summary>
///     Marks a request that anonymous callers may send, such as sign-in, sign-up or password reset. The actor check
///     lets it through and the default query / command scope does not apply to it. Requirements the request declares
///     itself still apply: <see cref="IRequiresRole" />, <see cref="IRequiresScope" /> and <see cref="IResourceScoped" />
///     are checked as usual, so combining them with this marker effectively requires a caller who has them.
/// </summary>
public interface IAllowAnonymous;

/// <summary>
///     Marks a request that requires its own scope instead of the default one
///     (<see cref="SynapseAuthorizationOptions.QueryScope" /> for queries,
///     <see cref="SynapseAuthorizationOptions.CommandScope" /> for commands). The scope applies even when the default
///     scopes are turned off.
/// </summary>
public interface IRequiresScope
{
    /// <summary>
    ///     The scope the caller must hold, checked with <see cref="ICurrentPrincipal.HasScope" />. A null, empty or
    ///     whitespace value is a bug in the request type: dispatching it throws an
    ///     <see cref="InvalidOperationException" />.
    /// </summary>
    string Scope { get; }
}

/// <summary>
///     Marks a request that only callers with a given role may send.
/// </summary>
public interface IRequiresRole
{
    /// <summary>
    ///     The role the caller must have, checked with <see cref="ICurrentPrincipal.IsInRole" />. A null, empty or
    ///     whitespace value is a bug in the request type: dispatching it throws an
    ///     <see cref="InvalidOperationException" />.
    /// </summary>
    string Role { get; }
}

/// <summary>
///     Marks a request aimed at one resource. The registered <see cref="IResourceAccessPolicy" /> decides whether the
///     caller may access it. For a request that targets a resource only for some arguments, implement
///     <see cref="IOptionallyResourceScoped" /> instead.
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

/// <summary>
///     Marks a request that targets one resource or none, depending on its arguments: a list with an optional account
///     filter, a report on the household or on one property, an item created standalone or under a parent. When
///     <see cref="OptionalResourceId" /> is <see langword="null" />, the resource check is skipped (the actor, role and
///     scope checks still run). Otherwise the request is checked like any <see cref="IResourceScoped" /> request: the
///     <see cref="IResourceAccessPolicy" /> receives it, and its <see cref="IResourceScoped.ResourceId" /> is
///     <see cref="OptionalResourceId" />.
/// </summary>
public interface IOptionallyResourceScoped : IResourceScoped
{
    /// <summary>
    ///     The identifier of the resource the request targets, or <see langword="null" /> when it targets none. An
    ///     empty or whitespace value is a bug in the request type: dispatching it throws an
    ///     <see cref="InvalidOperationException" />.
    /// </summary>
    string? OptionalResourceId { get; }

    // A separate name, not `new string? ResourceId`: a class member named ResourceId would implement both
    // interfaces' members at once, so this default would never run.
    string IResourceScoped.ResourceId => OptionalResourceId ?? throw new InvalidOperationException(
        $"{GetType().FullName} targets no resource ({nameof(OptionalResourceId)} is null), so it has no " +
        $"{nameof(IResourceScoped.ResourceId)}. The resource check skips such a request.");
}
