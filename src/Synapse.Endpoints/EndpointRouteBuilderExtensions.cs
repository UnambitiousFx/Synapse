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
}
