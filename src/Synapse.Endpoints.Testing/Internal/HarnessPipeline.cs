using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace UnambitiousFx.Synapse.Endpoints.Testing.Internal;

/// <summary>
///     Builds the routing pipeline one harness runs its requests through.
/// </summary>
/// <remarks>
///     The pipeline is the real one: <see cref="EndpointRoutingApplicationBuilderExtensions.UseRouting" />
///     and <c>UseEndpoints</c> around the library's own public
///     <see cref="EndpointRouteBuilderExtensions.MapEndpoint{TEndpoint}" />. Nothing here re-implements
///     route matching, which is the point — constraints, group prefixes and the
///     <c>405</c> a wrong verb produces are the framework's answers rather than approximations of them.
/// </remarks>
internal static class HarnessPipeline
{
    /// <summary>Builds the provider, pipeline and route description for one endpoint type.</summary>
    /// <typeparam name="TEndpoint">The endpoint type to map.</typeparam>
    /// <param name="services">The configured services.</param>
    /// <returns>The built provider, the request pipeline, and a human-readable route description.</returns>
    internal static (ServiceProvider Provider, RequestDelegate Pipeline, string RouteDescription) Build<TEndpoint>(
        IServiceCollection services)
        where TEndpoint : EndpointBase, new()
    {
        var provider = services.BuildServiceProvider();
        var app = new ApplicationBuilder(provider);

        IEndpointRouteBuilder? routes = null;
        app.UseRouting();
        app.UseEndpoints(builder =>
        {
            // Captured rather than read back from app.Properties: the callback runs synchronously
            // inside UseEndpoints, and the property key holding the builder is not public API.
            routes = builder;
            builder.MapEndpoint<TEndpoint>();
        });

        return (provider, app.Build(), Describe(routes!));
    }

    /// <summary>Renders the mapped routes as "GET /tasks/{taskId:guid}", for diagnostics.</summary>
    /// <param name="routes">The route builder the endpoint was mapped onto.</param>
    /// <returns>The description, comma-separated when the endpoint claims several verbs.</returns>
    private static string Describe(IEndpointRouteBuilder routes)
    {
        var descriptions = new List<string>();

        foreach (var dataSource in routes.DataSources)
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
                    descriptions.Add($"{method} {route.RoutePattern.RawText}");
                }
            }
        }

        return descriptions.Count > 0 ? string.Join(", ", descriptions) : "(no route)";
    }
}
