using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UnambitiousFx.Synapse.Endpoints.OpenApi.Internal;

namespace UnambitiousFx.Synapse.Endpoints.OpenApi;

/// <summary>Registers the container-level services Synapse's OpenAPI contributions need.</summary>
/// <remarks>
///     Separate from <see cref="OpenApiOptionsExtensions.AddSynapseEndpoints" /> because that one is
///     an <c>OpenApiOptions</c> extension, called from inside the <c>AddOpenApi(options =&gt; …)</c>
///     configuration delegate — which never has access to the <see cref="IServiceCollection" /> it
///     runs under, so a fix that needs its own DI registration cannot live there.
/// </remarks>
public static class OpenApiServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the fix that keeps a Synapse form-bound endpoint from crashing OpenAPI document
    ///     generation entirely. Call alongside <c>AddOpenApi(options =&gt; options.AddSynapseEndpoints())</c>
    ///     whenever any mapped endpoint accepts a form body.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The services, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services" /> is <see langword="null" />.</exception>
    /// <example>
    ///     <code>
    /// builder.Services.AddSynapseEndpointsOpenApi();
    /// builder.Services.AddOpenApi(options =&gt; options.AddSynapseEndpoints());
    ///     </code>
    /// </example>
    /// <remarks>
    ///     <para>
    ///         Why this needs its own call: <c>FormRequestMetadata.RequestType</c> is deliberately
    ///         <see langword="null" /> — a message
    ///         holding an <c>IFormFile</c> has no JSON schema to describe. Microsoft.AspNetCore.OpenApi's
    ///         own <c>EndpointMetadataApiDescriptionProvider</c> does not expect that combination — for
    ///         an endpoint with no method-level body parameter (every Synapse endpoint, which maps
    ///         through one untyped <c>HttpContext</c>) it synthesizes a body parameter typed
    ///         <c>RequestType ?? typeof(void)</c>, and the document service then throws trying to
    ///         describe <c>System.Void</c> as JSON — crashing the whole document, not just the one
    ///         operation. <see cref="Internal.FormRequestBodyDescriptionFixup" /> is a second
    ///         <c>IApiDescriptionProvider</c> that deletes that synthetic parameter before the document
    ///         service ever sees it, which is why registering it needs the container rather than the
    ///         options object.
    ///     </para>
    /// </remarks>
    public static IServiceCollection AddSynapseEndpointsOpenApi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IApiDescriptionProvider, FormRequestBodyDescriptionFixup>());
        return services;
    }
}
