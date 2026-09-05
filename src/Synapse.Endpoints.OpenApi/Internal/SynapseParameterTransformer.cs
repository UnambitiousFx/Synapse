using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using UnambitiousFx.Synapse.Endpoints.Internal;

namespace UnambitiousFx.Synapse.Endpoints.OpenApi.Internal;

/// <summary>
///     Adds the parameters and the form-body schema that Synapse's binders declare, which the
///     framework cannot infer.
/// </summary>
/// <remarks>
///     <para>
///         An operation transformer rather than endpoint metadata because there is no metadata type
///         that adds an operation parameter: the document's <c>parameters</c> array is built from the
///         route handler delegate's <c>MethodInfo</c> parameters, and Synapse maps
///         <c>context =&gt; …</c> — one <c>HttpContext</c> parameter, which the framework skips.
///     </para>
///     <para>
///         Scoped to endpoints carrying <c>SynapseEndpointMarker</c>, the same predicate
///         <c>ThrowOnDuplicateRoutes</c> uses, so a hand-written <c>app.MapGet</c> is never touched.
///     </para>
/// </remarks>
internal sealed class SynapseParameterTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;

        if (!metadata.OfType<SynapseEndpointMarker>().Any())
        {
            return Task.CompletedTask;
        }

        // Task 7 adds parameters here; Task 8 adds the form schema.
        return Task.CompletedTask;
    }
}
