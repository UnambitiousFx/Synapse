using Microsoft.AspNetCore.OpenApi;
using UnambitiousFx.Synapse.Endpoints.OpenApi.Internal;

namespace UnambitiousFx.Synapse.Endpoints.OpenApi;

/// <summary>Registers Synapse's OpenAPI contributions on an <see cref="OpenApiOptions" />.</summary>
public static class OpenApiOptionsExtensions
{
    /// <summary>
    ///     Declares the query, header and route parameters, and the form request-body schema, that
    ///     Synapse endpoints read.
    /// </summary>
    /// <param name="options">The OpenAPI options.</param>
    /// <returns>The options, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options" /> is <see langword="null" />.</exception>
    /// <example>
    ///     <code>
    /// builder.Services.AddOpenApi(options =&gt; options.AddSynapseEndpoints());
    ///     </code>
    /// </example>
    /// <remarks>
    ///     Opt-in, and additive: without this call the document is exactly what it was before this
    ///     package existed. Only endpoints Synapse mapped are affected.
    /// </remarks>
    public static OpenApiOptions AddSynapseEndpoints(this OpenApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.AddOperationTransformer<SynapseParameterTransformer>();
        return options;
    }
}
