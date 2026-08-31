using Microsoft.AspNetCore.Http.Metadata;

namespace UnambitiousFx.Synapse.Endpoints.Internal;

/// <summary>
///     Declares one response of an endpoint to OpenAPI. Supplied here because the framework's own
///     implementation of <see cref="IProducesResponseTypeMetadata" /> is internal.
/// </summary>
internal sealed class ProducesResponseMetadata : IProducesResponseTypeMetadata
{
    private static readonly string[] JsonContentType = ["application/json"];

    internal ProducesResponseMetadata(int statusCode,
        Type? type = null,
        string[]? contentTypes = null)
    {
        StatusCode = statusCode;

        // A response with no body is described as void rather than as null. Microsoft.AspNetCore.OpenApi
        // skips an IProducesResponseTypeMetadata whose Type is null outright, so a null here meant a
        // declared bodyless status code (a 204 from the void arity, or a Produces(304) on a low-level
        // endpoint) never appeared in the document at all — the metadata was there and the schema
        // generator quietly ignored it.
        Type = type ?? typeof(void);
        ContentTypes = type is null || type == typeof(void)
            ? []
            : contentTypes ?? JsonContentType;
    }

    public Type? Type { get; }

    public int StatusCode { get; }

    public IEnumerable<string> ContentTypes { get; }
}
