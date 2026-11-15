using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Authorization;

/// <summary>
///     Pipeline positions (<see cref="IOrderedPipelineBehavior.Order" />) of the authorization behaviors: actor, then
///     role, then scope, then resource, all in <see cref="PipelineStage.Authorization" /> (the band starting at
///     <c>PipelineStages.StartOf(PipelineStage.Authorization)</c>). Observability behaviors therefore wrap them, and
///     validation and transactions run inside them.
/// </summary>
/// <remarks>
///     Prefer placing your own behaviors by stage: <c>[PipelineBehavior(Stage = PipelineStage.Authorization, After =
///     typeof(ActorAuthorizationBehavior&lt;,&gt;))]</c>, or <c>ConfigureBehaviorOrdering</c> for a behavior the source
///     generator does not see. The gaps between the constants still allow <c>AuthorizationBehaviorOrder.Resource - 1</c>.
/// </remarks>
public static class AuthorizationBehaviorOrder
{
    // PipelineStages.StartOf(PipelineStage.Authorization); a const cannot call it.
    private const uint Stage = 0x4000_0000;

    /// <summary>Position of <see cref="ActorAuthorizationBehavior{TRequest,TResult}" />.</summary>
    public const uint Actor = Stage + 1_000;

    /// <summary>Position of <see cref="RoleAuthorizationBehavior{TRequest,TResult}" />.</summary>
    public const uint Role = Stage + 2_000;

    /// <summary>Position of <see cref="ScopeAuthorizationBehavior{TRequest,TResult}" />.</summary>
    public const uint Scope = Stage + 3_000;

    /// <summary>Position of <see cref="ResourceAuthorizationBehavior{TRequest,TResult}" />.</summary>
    public const uint Resource = Stage + 4_000;
}
