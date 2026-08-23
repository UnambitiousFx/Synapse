using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using UnambitiousFx.Synapse.Endpoints.Binding;
using UnambitiousFx.Synapse.Endpoints.Internal;

namespace UnambitiousFx.Synapse.Endpoints;

/// <summary>Extension methods that map Synapse endpoints onto the route table.</summary>
public static class EndpointRouteBuilderExtensions
{
    /// <summary>
    ///     Maps a single endpoint. Generated endpoint groups are made of these calls, so mapping by
    ///     hand and mapping from a generated group behave identically.
    /// </summary>
    /// <typeparam name="TEndpoint">The endpoint type.</typeparam>
    /// <param name="endpoints">The route builder.</param>
    /// <returns>The route handler builder, for further configuration.</returns>
    /// <exception cref="InvalidOperationException">
    ///     The endpoint declares a group via <c>InGroupAttribute</c> but no group factory was
    ///     registered for it.
    /// </exception>
    public static RouteHandlerBuilder MapEndpoint<TEndpoint>(this IEndpointRouteBuilder endpoints)
        where TEndpoint : EndpointBase, new()
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var metadata = EndpointRegistry.GetMetadata<TEndpoint>();
        var target = endpoints;

        if (metadata.GroupType is not null)
        {
            if (metadata.GroupFactory is null)
            {
                throw new InvalidOperationException(
                    $"Endpoint '{typeof(TEndpoint).Name}' declares group '{metadata.GroupType.Name}' but no " +
                    "group factory was registered. The Synapse.Endpoints analyzer emits the factory alongside " +
                    "the route metadata; verify it is enabled for the assembly declaring this endpoint.");
            }

            target = GroupCache.Resolve(endpoints, metadata.GroupType, metadata.GroupFactory);
        }

        var endpoint = new TEndpoint();
        var descriptor = endpoint.CreateDescriptor(metadata);
        return EndpointMapper.Map(target, descriptor);
    }

    /// <summary>
    ///     Maps the endpoints of one or more assemblies and verifies that no two endpoints claim the
    ///     same HTTP method and route.
    /// </summary>
    /// <param name="endpoints">The route builder.</param>
    /// <param name="groups">The generated endpoint groups, one per assembly.</param>
    /// <returns>The route builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="endpoints" /> or <paramref name="groups" /> is <see langword="null" />.
    /// </exception>
    /// <exception cref="InvalidOperationException">Two endpoints claim the same method and route.</exception>
    /// <remarks>
    ///     The duplicate check runs once, after every group has mapped, so it sees the full route
    ///     table with group prefixes already applied. That is why it is a startup check rather than a
    ///     compile-time diagnostic: a group's prefix is a runtime value.
    /// </remarks>
    public static IEndpointRouteBuilder MapSynapseEndpoints(this IEndpointRouteBuilder endpoints,
        params IEndpointGroup[] groups)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(groups);

        foreach (var group in groups)
        {
            group.Map(endpoints);
        }

        ThrowOnDuplicateRoutes(endpoints);
        return endpoints;
    }

    /// <summary>
    ///     Walks every mapped endpoint's route pattern and throws when two of them claim the same
    ///     HTTP method and route.
    /// </summary>
    /// <param name="endpoints">The route builder whose data sources are inspected.</param>
    /// <exception cref="InvalidOperationException">Two endpoints claim the same method and route.</exception>
    private static void ThrowOnDuplicateRoutes(IEndpointRouteBuilder endpoints)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicates = new List<string>();

        foreach (var dataSource in endpoints.DataSources)
        {
            foreach (var endpoint in dataSource.Endpoints)
            {
                if (endpoint is not RouteEndpoint route)
                {
                    continue;
                }

                var methods = route.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
                foreach (var method in methods)
                {
                    var key = $"{method} {route.RoutePattern.RawText}";
                    if (!seen.Add(key))
                    {
                        duplicates.Add(key);
                    }
                }
            }
        }

        if (duplicates.Count > 0)
        {
            throw new InvalidOperationException(
                "More than one Synapse endpoint claims the same HTTP method and route: " +
                string.Join(", ", duplicates) +
                ". Give each endpoint a distinct route, or check whether a group prefix collides with " +
                "an endpoint's own route template.");
        }
    }
}
