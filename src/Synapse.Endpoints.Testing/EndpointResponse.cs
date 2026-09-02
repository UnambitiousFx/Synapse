using System.Text;
using Microsoft.AspNetCore.Http;

namespace UnambitiousFx.Synapse.Endpoints.Testing;

/// <summary>
///     What an endpoint wrote in response to a harness request.
/// </summary>
/// <remarks>
///     Captured after the pipeline completes, so a streaming endpoint's body is the whole materialised
///     sequence rather than a stream still being written.
/// </remarks>
public sealed class EndpointResponse
{
    private readonly Lazy<string> _body;

    /// <summary>Initializes a new instance of the <see cref="EndpointResponse" /> class.</summary>
    /// <param name="statusCode">The status code written.</param>
    /// <param name="headers">The response headers.</param>
    /// <param name="contentType">The content type, if one was written.</param>
    /// <param name="bodyBytes">The response body.</param>
    internal EndpointResponse(int statusCode,
        IHeaderDictionary headers,
        string? contentType,
        byte[] bodyBytes)
    {
        StatusCode = statusCode;
        Headers = headers;
        ContentType = contentType;
        BodyBytes = bodyBytes;
        _body = new Lazy<string>(() => Encoding.UTF8.GetString(bodyBytes));
    }

    /// <summary>Gets the status code the endpoint wrote.</summary>
    public int StatusCode { get; }

    /// <summary>Gets the response headers.</summary>
    public IHeaderDictionary Headers { get; }

    /// <summary>Gets the response content type, or <see langword="null" /> when none was written.</summary>
    public string? ContentType { get; }

    /// <summary>Gets the raw response body.</summary>
    public byte[] BodyBytes { get; }

    /// <summary>Gets the response body decoded as UTF-8.</summary>
    public string Body => _body.Value;
}
