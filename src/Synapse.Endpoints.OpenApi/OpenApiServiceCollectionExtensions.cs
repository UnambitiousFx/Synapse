using System.Linq;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UnambitiousFx.Synapse.Endpoints.OpenApi.Internal;

namespace UnambitiousFx.Synapse.Endpoints.OpenApi;

/// <summary>Registers Synapse's OpenAPI contributions.</summary>
public static class OpenApiServiceCollectionExtensions
{
    /// <summary>
    ///     Declares the query, header and route parameters, and the form request-body schema, that
    ///     Synapse endpoints read — and keeps a form-bound endpoint from crashing document generation
    ///     entirely. The one call every Synapse app needs, whatever <c>AddOpenApi</c> document names it
    ///     uses and whichever order the two calls happen in.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The services, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services" /> is <see langword="null" />.</exception>
    /// <example>
    ///     <code>
    /// builder.Services.AddSynapseEndpointsOpenApi();
    /// builder.Services.AddOpenApi();
    ///     </code>
    /// </example>
    /// <remarks>
    ///     <para>
    ///         One call, not two. An earlier iteration split this into an <c>OpenApiOptions</c>
    ///         extension (<c>options.AddSynapseEndpoints()</c>, contributing the operation transformer)
    ///         and this <see cref="IServiceCollection" /> extension (contributing
    ///         <see cref="Internal.FormRequestBodyDescriptionFixup" />, the fix described below) — two
    ///         calls that fail in <em>opposite</em> directions when only one is made: the fixup alone
    ///         means no parameters or form schema are ever declared, and the transformer alone means a
    ///         crashed document the moment any endpoint accepts a form body. An API that cannot be used
    ///         correctly on its own should not exist, so both are registered here together. The split
    ///         existed only because, at the time, the fixup did not — once the framework bug below was
    ///         handled, the constraint that justified two entry points went with it.
    ///     </para>
    ///     <para>
    ///         The transformer is registered via <c>services.ConfigureAll&lt;OpenApiOptions&gt;(…)</c>
    ///         rather than requiring a document-specific <c>AddOpenApi(options =&gt; …)</c> callback:
    ///         <c>ConfigureAll</c> configures with a <see langword="null" /> options name, and
    ///         <c>ConfigureNamedOptions&lt;TOptions&gt;.Configure</c> runs a null-named configuration
    ///         against every named options instance — so this reaches whatever document name(s)
    ///         <c>AddOpenApi</c> is called with, in either call order, including multiple documents.
    ///     </para>
    ///     <para>
    ///         <b>The bug this also fixes:</b> <c>FormRequestMetadata.RequestType</c> is deliberately
    ///         <see langword="null" /> — a message holding an <c>IFormFile</c> has no JSON schema to
    ///         describe. Microsoft.AspNetCore.OpenApi's own <c>EndpointMetadataApiDescriptionProvider</c>
    ///         does not expect that combination: for an endpoint with no method-level body parameter
    ///         (every Synapse endpoint, which maps through one untyped <c>HttpContext</c>) it
    ///         synthesizes a body parameter typed <c>RequestType ?? typeof(void)</c>, and the document
    ///         service then throws trying to describe <c>System.Void</c> as JSON — crashing the whole
    ///         document, not just the one operation.
    ///         <see cref="Internal.FormRequestBodyDescriptionFixup" /> is a second
    ///         <c>IApiDescriptionProvider</c> that deletes that synthetic parameter before the document
    ///         service ever sees it, which is why registering it needs the container rather than an
    ///         options object.
    ///     </para>
    ///     <para>
    ///         Idempotent: a second call is a no-op, guarded by a private marker service, so an
    ///         accidental double call (a shared startup helper invoked twice, say) does not register the
    ///         transformer or the fixup twice.
    ///     </para>
    /// </remarks>
    public static IServiceCollection AddSynapseEndpointsOpenApi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(RegistrationMarker)))
        {
            return services;
        }

        services.AddSingleton<RegistrationMarker>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IApiDescriptionProvider, FormRequestBodyDescriptionFixup>());
        services.ConfigureAll<OpenApiOptions>(
            options => options.AddOperationTransformer<SynapseParameterTransformer>());

        return services;
    }

    /// <summary>Marks a service collection as already carrying Synapse's OpenAPI registrations.</summary>
    /// <remarks>
    ///     Never resolved — its only job is to exist in <see cref="IServiceCollection" /> so a second
    ///     <see cref="AddSynapseEndpointsOpenApi" /> call can detect the first one. A
    ///     <c>ConfigureAll</c> call has no such marker of its own (it just adds another
    ///     <c>IConfigureOptions&lt;OpenApiOptions&gt;</c> entry every time), so without this the second
    ///     call would add a second operation transformer instance.
    /// </remarks>
    private sealed class RegistrationMarker;
}
