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
    public static RouteHandlerBuilder MapEndpoint<TEndpoint>(this IEndpointRouteBuilder endpoints)
        where TEndpoint : EndpointBase, new()
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var endpoint = new TEndpoint();
        var descriptor = endpoint.CreateDescriptor(EndpointRegistry.GetMetadata<TEndpoint>());
        return EndpointMapper.Map(endpoints, descriptor);
    }
}
