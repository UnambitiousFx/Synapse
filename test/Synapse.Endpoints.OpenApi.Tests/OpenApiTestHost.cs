using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace UnambitiousFx.Synapse.Endpoints.OpenApi.Tests;

/// <summary>
///     Generates a real OpenAPI document for one endpoint, the way the framework generates the one
///     <c>MapOpenApi</c> serves.
/// </summary>
/// <remarks>
///     <para>
///         A started host rather than a hand-assembled pipeline: the document is built from the
///         <c>EndpointDataSource</c> registered in DI, and a <c>WebApplication</c> only publishes the
///         routes mapped onto it when it starts. <c>UseTestServer</c> keeps that start from binding a
///         socket.
///     </para>
///     <para>
///         The document provider is a <em>keyed</em> service, keyed by document name — "v1" for the
///         default document <c>AddOpenApi()</c> configures.
///     </para>
/// </remarks>
internal static class OpenApiTestHost
{
    private const string DocumentName = "v1";

    /// <summary>Maps one Synapse endpoint and returns the document generated for it.</summary>
    /// <typeparam name="TEndpoint">The endpoint type to map.</typeparam>
    /// <returns>The generated document.</returns>
    internal static Task<OpenApiDocument> GenerateAsync<TEndpoint>()
        where TEndpoint : SynapseEndpoint, new()
    {
        return GenerateAsync(app => app.MapEndpoint<TEndpoint>());
    }

    /// <summary>Maps two Synapse endpoints into one document and returns it.</summary>
    /// <typeparam name="TEndpointA">The first endpoint type to map.</typeparam>
    /// <typeparam name="TEndpointB">The second endpoint type to map.</typeparam>
    /// <returns>The generated document, carrying both endpoints' operations.</returns>
    /// <remarks>
    ///     For proving isolation: the framework bug this package works around (a form-bound endpoint
    ///     crashing the <em>entire</em> document) and the fixup's own blast radius (it must touch only
    ///     the operation it targets) are both properties of a multi-endpoint document — a single-
    ///     endpoint document cannot exercise either.
    /// </remarks>
    internal static Task<OpenApiDocument> GenerateAsync<TEndpointA, TEndpointB>()
        where TEndpointA : SynapseEndpoint, new()
        where TEndpointB : SynapseEndpoint, new()
    {
        return GenerateAsync(app =>
        {
            app.MapEndpoint<TEndpointA>();
            app.MapEndpoint<TEndpointB>();
        });
    }

    /// <summary>Maps a hand-written minimal-API route and returns the document generated for it.</summary>
    /// <returns>The generated document.</returns>
    /// <remarks>
    ///     The control for every other case here: this route carries no Synapse metadata, so the
    ///     transformer must leave its operation exactly as the framework built it.
    /// </remarks>
    internal static Task<OpenApiDocument> GenerateWithHandWrittenRouteAsync()
    {
        return GenerateAsync(app => app.MapGet("/version", () => "1.0"));
    }

    private static async Task<OpenApiDocument> GenerateAsync(Action<WebApplication> map)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSynapseEndpointsOpenApi();
        builder.Services.AddOpenApi();

        await using var app = builder.Build();
        map(app);
        await app.StartAsync();

        var documents = app.Services.GetRequiredKeyedService<IOpenApiDocumentProvider>(DocumentName);
        var document = await documents.GetOpenApiDocumentAsync();
        await app.StopAsync();

        return document;
    }
}
