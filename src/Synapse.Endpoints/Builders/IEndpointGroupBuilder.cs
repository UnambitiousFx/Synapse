using Microsoft.AspNetCore.Routing;

namespace UnambitiousFx.Synapse.Endpoints.Builders;

/// <summary>Configures a group of endpoints, contributing a route prefix and shared metadata.</summary>
public interface IEndpointGroupBuilder
{
    /// <summary>Sets the route prefix applied to every endpoint in the group.</summary>
    /// <param name="prefix">The prefix, for example <c>/tasks</c>.</param>
    /// <returns>The builder, for chaining.</returns>
    IEndpointGroupBuilder Prefix(string prefix);

    /// <summary>Adds OpenAPI tags to every endpoint in the group.</summary>
    /// <param name="tags">The tags.</param>
    /// <returns>The builder, for chaining.</returns>
    IEndpointGroupBuilder Tag(params string[] tags);

    /// <summary>Requires authorization for every endpoint in the group.</summary>
    /// <param name="policies">The policy names, or none for the default policy.</param>
    /// <returns>The builder, for chaining.</returns>
    IEndpointGroupBuilder RequireAuthorization(params string[] policies);

    /// <summary>Escape hatch onto the underlying <see cref="RouteGroupBuilder" />.</summary>
    /// <param name="configure">The configuration callback, applied at startup.</param>
    /// <returns>The builder, for chaining.</returns>
    IEndpointGroupBuilder Raw(Action<RouteGroupBuilder> configure);
}
