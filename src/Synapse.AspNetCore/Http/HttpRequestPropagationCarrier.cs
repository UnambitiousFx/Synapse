using Microsoft.AspNetCore.Http;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.AspNetCore.Http;

/// <summary>
///     An <see cref="IPropagationCarrier" /> over the headers of an incoming <see cref="HttpRequest" />.
/// </summary>
public sealed class HttpRequestPropagationCarrier : IPropagationCarrier
{
    private readonly IHeaderDictionary _headers;

    /// <summary>
    ///     Wraps an incoming request.
    /// </summary>
    /// <param name="request">The request whose headers hold the propagated state.</param>
    public HttpRequestPropagationCarrier(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _headers = request.Headers;
    }

    /// <inheritdoc />
    public bool TryGetValue(string key,
        out string? value)
    {
        value = null;

        // A duplicated header is ambiguous. Relying on the StringValues-to-string conversion would resolve it
        // silently and differently depending on the count, so the check is explicit.
        if (!_headers.TryGetValue(key, out var values) ||
            values.Count != 1)
        {
            return false;
        }

        value = values[0];
        return value is not null;
    }

    /// <inheritdoc />
    public void Set(string key,
        string value)
    {
        _headers[key] = value;
    }
}
