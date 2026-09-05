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
    public async Task TransformAsync(OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;

        if (!metadata.OfType<SynapseEndpointMarker>().Any())
        {
            return;
        }

        var declared = metadata.OfType<BoundParametersMetadata>().FirstOrDefault();
        if (declared is not null)
        {
            await ApplyParametersAsync(operation, declared, context, cancellationToken);
        }

        // Task 8 adds the form schema.
    }

    private static async Task ApplyParametersAsync(OpenApiOperation operation,
        BoundParametersMetadata declared,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        operation.Parameters ??= [];

        foreach (var parameter in declared.Parameters)
        {
            var location = Translate(parameter.Location);
            var existing = operation.Parameters
                .FirstOrDefault(p => p.In == location
                                     && string.Equals(p.Name, parameter.Name, StringComparison.Ordinal));

            if (existing is not null)
            {
                // Unreachable as of .NET 10 — verified, not assumed: a Synapse route handler takes
                // one HttpContext parameter, and the framework builds an operation's parameters from
                // the handler's MethodInfo alone, adding nothing for route-template segments it does
                // not match. A Synapse operation therefore arrives here with no parameters at all,
                // path parameters included, and every parameter in the document is one this
                // transformer added.
                //
                // Kept anyway, deliberately, because of what it guards: not an exception but an
                // *invalid document*. Two entries for one path parameter is illegal OpenAPI and some
                // client generators reject it outright, so a framework version that starts inferring
                // path parameters would silently produce a broken document with no test here failing.
                // Reconciling instead of adding costs four lines and converts that into a non-event.
                //
                // The cast is forced by Microsoft.OpenApi 2.x: the list is IList<IOpenApiParameter>
                // and that interface's Schema and Required are get-only, so only the concrete type
                // can be filled in. A reference entry ($ref to a component) is left exactly as it is
                // — it has nothing local to reconcile, and leaving it alone beats both throwing and
                // adding a second entry. The `continue` below applies either way, so the
                // never-duplicate guarantee does not depend on the concrete type.
                if (existing is OpenApiParameter concrete)
                {
                    concrete.Schema ??= await SchemaFactory.CreateAsync(
                        parameter.ValueType, parameter.IsArray, context, cancellationToken);
                    concrete.Required = concrete.Required || parameter.Required;
                }

                continue;
            }

            operation.Parameters.Add(new OpenApiParameter
            {
                Name = parameter.Name,
                In = location,
                Required = parameter.Required,
                Schema = await SchemaFactory.CreateAsync(
                    parameter.ValueType, parameter.IsArray, context, cancellationToken)
            });
        }
    }

    private static ParameterLocation Translate(BoundParameterLocation location)
    {
        return location switch
        {
            BoundParameterLocation.Path => ParameterLocation.Path,
            BoundParameterLocation.Query => ParameterLocation.Query,
            BoundParameterLocation.Header => ParameterLocation.Header,

            // Unreachable: the enum has three members and all three are mapped. Throwing rather
            // than defaulting means a member added later fails loudly here instead of being
            // silently documented as a query parameter.
            _ => throw new ArgumentOutOfRangeException(nameof(location), location, null)
        };
    }
}
