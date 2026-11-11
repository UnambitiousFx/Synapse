using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Authorization;

/// <summary>
///     Pipeline positions (<see cref="IOrderedPipelineBehavior.Order" />) of the authorization behaviors: actor, then
///     role, then scope, then resource. They run early, after the built-in <see cref="IOrderedPipelineBehavior.First" />
///     behaviors and before validation (<see cref="IOrderedPipelineBehavior.Last" />). The gaps leave room for your own
///     behaviors between two checks, for example one that loads a tenant before the resource check.
/// </summary>
public static class AuthorizationBehaviorOrder
{
    /// <summary>Position of <see cref="ActorAuthorizationBehavior{TRequest,TResult}" />.</summary>
    public const uint Actor = 1_000;

    /// <summary>Position of <see cref="RoleAuthorizationBehavior{TRequest,TResult}" />.</summary>
    public const uint Role = 2_000;

    /// <summary>Position of <see cref="ScopeAuthorizationBehavior{TRequest,TResult}" />.</summary>
    public const uint Scope = 3_000;

    /// <summary>Position of <see cref="ResourceAuthorizationBehavior{TRequest,TResult}" />.</summary>
    public const uint Resource = 4_000;
}
