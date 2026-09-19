using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using UnambitiousFx.Synapse.Endpoints.Builders;

namespace UnambitiousFx.Synapse.Endpoints.Internal;

/// <summary>
///     Creates one <see cref="RouteGroupBuilder" /> per group type per root route builder, so many
///     endpoints in the same group share a single <c>MapGroup</c> call and its group's
///     <c>Configure</c> runs only once.
/// </summary>
internal static class GroupCache
{
    private static readonly ConditionalWeakTable<IEndpointRouteBuilder, Dictionary<Type, RouteGroupBuilder>> Cache = new();

    /// <summary>
    ///     Resolves the route builder for a group, creating and configuring it on first use for the
    ///     given root, and reusing it for every subsequent endpoint declaring the same group.
    /// </summary>
    /// <param name="root">The root route builder the group is mapped from.</param>
    /// <param name="groupType">The group type, used as the cache key.</param>
    /// <param name="factory">Creates the group instance so its <c>Configure</c> can run.</param>
    /// <returns>The group's route builder; endpoints in the group are mapped onto it.</returns>
    internal static IEndpointRouteBuilder Resolve(IEndpointRouteBuilder root,
        Type groupType,
        Func<EndpointGroup> factory)
    {
        var groups = Cache.GetOrCreateValue(root);
        if (groups.TryGetValue(groupType, out var existing))
        {
            return existing;
        }

        var groupBuilder = new EndpointGroupBuilder();
        factory().Configure(groupBuilder);

        var created = root.MapGroup(groupBuilder.GetPrefix());
        groupBuilder.Apply(created);

        groups[groupType] = created;
        return created;
    }
}
