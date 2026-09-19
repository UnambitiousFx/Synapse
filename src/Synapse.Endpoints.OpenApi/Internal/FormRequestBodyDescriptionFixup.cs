using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using UnambitiousFx.Synapse.Endpoints.Internal;

namespace UnambitiousFx.Synapse.Endpoints.OpenApi.Internal;

/// <summary>
///     Removes the synthetic, <c>void</c>-typed body parameter
///     <c>EndpointMetadataApiDescriptionProvider</c> adds for a Synapse form-bound endpoint, before
///     <c>Microsoft.AspNetCore.OpenApi</c> tries to build a schema for it and crashes.
/// </summary>
/// <remarks>
///     <para>
///         <c>FormRequestMetadata.RequestType</c> is deliberately <see langword="null" /> — a message
///         holding an <c>IFormFile</c> has no JSON schema to describe. The framework's own
///         <c>EndpointMetadataApiDescriptionProvider</c> does not anticipate that: an endpoint with no
///         method-level body or form parameter (true of every Synapse endpoint, which maps through one
///         untyped <c>HttpContext</c> parameter — see <c>SynapseParameterTransformer</c>'s own remarks)
///         but carrying an <c>IAcceptsMetadata</c> gets a synthetic <c>ApiParameterDescription</c> with
///         <c>Source = BindingSource.Body</c> and <c>Type = RequestType ?? typeof(void)</c>. For a
///         form-bound endpoint that type is <c>typeof(void)</c>, and
///         <c>OpenApiDocumentService.GetJsonRequestBody</c> then asks <c>JsonSchemaExporter</c> to
///         describe it — <c>System.Text.Json</c> refuses to describe <c>System.Void</c> and throws
///         <see cref="InvalidOperationException" />, which is unhandled all the way up through
///         <c>GetOpenApiPathsAsync</c> and takes down the <em>entire</em> document, not just this one
///         operation. Verified against a plain minimal-API endpoint carrying no Synapse code at all:
///         the crash is a framework interaction, not something particular to this library.
///     </para>
///     <para>
///         Deleting that one synthetic parameter here — before
///         <c>OpenApiDocumentService</c> ever sees it — makes <c>ApiDescription.TryGetBodyParameter</c>
///         and <c>TryGetFormParameters</c> both report nothing for the operation, so the framework's
///         own request-body construction is skipped entirely and <c>OpenApiOperation.RequestBody</c>
///         stays <see langword="null" /> — exactly where <c>SynapseParameterTransformer</c> expects to
///         find it before replacing it with the field-by-field schema.
///     </para>
///     <para>
///         <see cref="Order" /> is <c>-1050</c>: after
///         <c>EndpointMetadataApiDescriptionProvider</c>'s <c>-1100</c> (<c>IApiDescriptionProvider</c>s
///         run <c>OnProvidersExecuting</c> in ascending order, and the parameter removed here does not
///         exist until that provider's pass has added it), and deliberately not <c>-1000</c> — that
///         value exactly ties MVC's own <c>DefaultApiDescriptionProvider</c>. Harmless today, since
///         that provider only handles <c>ControllerActionDescriptor</c> and never sees a Synapse
///         endpoint, but <c>-1050</c> sits unambiguously between the two and removes the question.
///     </para>
/// </remarks>
internal sealed class FormRequestBodyDescriptionFixup : IApiDescriptionProvider
{
    /// <inheritdoc />
    public int Order => -1050;

    /// <inheritdoc />
    public void OnProvidersExecuting(ApiDescriptionProviderContext context)
    {
        foreach (var description in context.Results)
        {
            var hasFormMetadata = description.ActionDescriptor.EndpointMetadata?
                .OfType<FormRequestMetadata>().Any() == true;

            if (!hasFormMetadata)
            {
                continue;
            }

            for (var i = description.ParameterDescriptions.Count - 1; i >= 0; i--)
            {
                var parameter = description.ParameterDescriptions[i];
                if (parameter.Source == BindingSource.Body && parameter.Type == typeof(void))
                {
                    description.ParameterDescriptions.RemoveAt(i);
                }
            }
        }
    }

    /// <inheritdoc />
    public void OnProvidersExecuted(ApiDescriptionProviderContext context)
    {
        // Nothing to do: the fix-up only needs to run once, before OpenApiDocumentService reads the
        // finished ApiDescriptionGroupCollection, and OnProvidersExecuting already ran by then.
    }
}
