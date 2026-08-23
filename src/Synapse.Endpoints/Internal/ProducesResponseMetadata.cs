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
        Type = type;
        ContentTypes = type is null
            ? []
            : contentTypes ?? JsonContentType;
    }

    public Type? Type { get; }

    public int StatusCode { get; }

    public IEnumerable<string> ContentTypes { get; }
}
