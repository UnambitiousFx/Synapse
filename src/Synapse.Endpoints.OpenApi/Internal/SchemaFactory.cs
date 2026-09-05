using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace UnambitiousFx.Synapse.Endpoints.OpenApi.Internal;

/// <summary>Builds the schema for one bound value, scalar or array.</summary>
/// <remarks>
///     Goes through the document's own schema generation rather than a hand-rolled
///     <c>Type</c>-to-<c>"integer"</c> table. A local table would describe an enum, a
///     <c>DateOnly</c>, or a type served by a custom value parser differently here than the same
///     type is described inside a response body — two descriptions of one type in one document.
///     <c>GetOrCreateSchemaAsync</c> also runs any registered <c>IOpenApiSchemaTransformer</c>, so a
///     consumer's own schema customisation applies to parameters too.
/// </remarks>
internal static class SchemaFactory
{
    /// <summary>Builds the schema for one bound value.</summary>
    /// <param name="valueType">The scalar type, or the element type when <paramref name="isArray" /> is set.</param>
    /// <param name="isArray">Whether the parameter repeats, so its schema is an array.</param>
    /// <param name="context">The transformer context whose schema generation is used.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The schema, never <see langword="null" />.</returns>
    internal static async Task<OpenApiSchema> CreateAsync(Type valueType,
        bool isArray,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var element = await CreateScalarAsync(valueType, context, cancellationToken);

        return isArray
            ? new OpenApiSchema { Type = JsonSchemaType.Array, Items = element }
            : element;
    }

    private static async Task<OpenApiSchema> CreateScalarAsync(Type valueType,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            return await context.GetOrCreateSchemaAsync(valueType, cancellationToken: cancellationToken);
        }
        catch (Exception)
        {
            // A named parameter with a vague type is strictly more useful than a silently absent
            // one, which is the status quo this feature exists to end. Document generation must
            // never fail because of this transformer: the OpenAPI endpoint regenerates per request,
            // so a throw takes the whole document down.
            return new OpenApiSchema();
        }
    }
}
