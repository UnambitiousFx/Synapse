using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

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
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly Lazy<string> _body;

    /// <summary>Initializes a new instance of the <see cref="EndpointResponse" /> class.</summary>
    /// <param name="statusCode">The status code written.</param>
    /// <param name="headers">The response headers.</param>
    /// <param name="contentType">The content type, if one was written.</param>
    /// <param name="bodyBytes">The response body.</param>
    /// <param name="jsonOptions">
    ///     The application's own JSON options — the same ones the endpoint serialized the body with.
    /// </param>
    internal EndpointResponse(int statusCode,
        IHeaderDictionary headers,
        string? contentType,
        byte[] bodyBytes,
        JsonSerializerOptions jsonOptions)
    {
        StatusCode = statusCode;
        Headers = headers;
        ContentType = contentType;
        BodyBytes = bodyBytes;
        _jsonOptions = jsonOptions;
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

    /// <summary>Deserializes the response body as JSON.</summary>
    /// <typeparam name="TValue">The type to deserialize to.</typeparam>
    /// <returns>The deserialized value.</returns>
    /// <remarks>Reads with the application's own JSON options, the same ones the endpoint serialized with.</remarks>
    /// <exception cref="InvalidOperationException">The body is not valid JSON for <typeparamref name="TValue" />.</exception>
    public TValue? ReadJson<TValue>()
    {
        try
        {
            return JsonSerializer.Deserialize<TValue>(BodyBytes, _jsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"The response body could not be read as '{typeof(TValue).Name}'. The response was " +
                $"{StatusCode} ({ContentType ?? "no content type"}): {Body}",
                exception);
        }
    }

    /// <summary>Reads the body as the validation problem a binding failure produces.</summary>
    /// <returns>The problem details, including the per-field errors.</returns>
    /// <remarks>Reads with the application's own JSON options, the same ones the endpoint serialized with.</remarks>
    /// <exception cref="InvalidOperationException">The body is not a validation problem.</exception>
    public HttpValidationProblemDetails ReadValidationProblem()
    {
        return ReadJson<HttpValidationProblemDetails>()
               ?? throw new InvalidOperationException(
                   $"The response body was null, so it is not a validation problem. The response was " +
                   $"{StatusCode} ({ContentType ?? "no content type"}).");
    }

    /// <summary>Reads the body as problem details.</summary>
    /// <returns>The problem details.</returns>
    /// <remarks>Reads with the application's own JSON options, the same ones the endpoint serialized with.</remarks>
    /// <exception cref="InvalidOperationException">The body is not problem details.</exception>
    public ProblemDetails ReadProblem()
    {
        return ReadJson<ProblemDetails>()
               ?? throw new InvalidOperationException(
                   $"The response body was null, so it is not problem details. The response was " +
                   $"{StatusCode} ({ContentType ?? "no content type"}).");
    }

    /// <summary>Renders the status, content type and body, for assertion failure messages.</summary>
    /// <returns>The description, appended to every <see cref="Assertions.EndpointAssertionException" /> message.</returns>
    internal string Describe()
    {
        var body = Body.Length == 0 ? "(empty body)" : Body;
        return $"The response was {StatusCode} ({ContentType ?? "no content type"}): {body}";
    }
}
