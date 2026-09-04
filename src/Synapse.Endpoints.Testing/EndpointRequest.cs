using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace UnambitiousFx.Synapse.Endpoints.Testing;

/// <summary>
///     One request to send through a harness. Build it fluently, then <see cref="SendAsync" />.
/// </summary>
public sealed class EndpointRequest
{
    // The boundary as declared in the Content-Type header; the delimiter lines written into the body
    // are "--" plus this value, per RFC 2046. Fixed rather than random so a failing multipart test
    // shows a stable, reviewable body instead of a boundary that differs on every run.
    private const string MultipartBoundaryValue = "------------------------synapse";

    private readonly IServiceProvider _provider;
    private readonly RequestDelegate _pipeline;
    private readonly string _routeDescription;
    private readonly string _method;
    private readonly string _path;
    private QueryString _query;
    private readonly HeaderDictionary _headers = [];
    private byte[]? _body;
    private string? _contentType;

    /// <summary>Initializes a new instance of the <see cref="EndpointRequest" /> class.</summary>
    /// <param name="provider">The harness's service provider.</param>
    /// <param name="pipeline">The harness's request pipeline.</param>
    /// <param name="routeDescription">The mapped route, used in the unmatched-URL diagnostic.</param>
    /// <param name="method">The HTTP method.</param>
    /// <param name="url">The URL, with an optional query string.</param>
    internal EndpointRequest(IServiceProvider provider,
        RequestDelegate pipeline,
        string routeDescription,
        string method,
        string url)
    {
        _provider = provider;
        _pipeline = pipeline;
        _routeDescription = routeDescription;
        _method = method;

        var separator = url.IndexOf('?', StringComparison.Ordinal);
        if (separator < 0)
        {
            _path = url;
            _query = QueryString.Empty;
        }
        else
        {
            _path = url[..separator];
            _query = new QueryString(url[separator..]);
        }
    }

    /// <summary>Appends a query-string value.</summary>
    /// <param name="name">The parameter name.</param>
    /// <param name="value">The value; <see langword="null" /> sends the parameter with an empty value.</param>
    /// <returns>The request, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name" /> is <see langword="null" />.</exception>
    public EndpointRequest Query(string name,
        string? value)
    {
        ArgumentNullException.ThrowIfNull(name);

        _query = _query.Add(name, value ?? string.Empty);
        return this;
    }

    /// <summary>Sets a request header, replacing any previous value for the same name.</summary>
    /// <param name="name">The header name.</param>
    /// <param name="value">The header value.</param>
    /// <returns>The request, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name" /> or <paramref name="value" /> is <see langword="null" />.</exception>
    public EndpointRequest Header(string name,
        string value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);

        _headers[name] = value;
        return this;
    }

    /// <summary>Sets the <c>Accept</c> header.</summary>
    /// <param name="mediaType">The media type to request, such as <c>text/event-stream</c>.</param>
    /// <returns>The request, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="mediaType" /> is <see langword="null" />.</exception>
    public EndpointRequest Accept(string mediaType)
    {
        return Header(HeaderNames.Accept, mediaType);
    }

    /// <summary>Sends <paramref name="body" /> as a JSON request body.</summary>
    /// <typeparam name="TBody">The body type.</typeparam>
    /// <param name="body">The value to serialize.</param>
    /// <returns>The request, for chaining.</returns>
    /// <remarks>
    ///     Serialized with the harness's own <c>JsonOptions</c>, so a test that calls
    ///     <c>ConfigureHttpJsonOptions</c> — to install a source-generated context, say — writes the
    ///     request the same way the endpoint reads it.
    /// </remarks>
    public EndpointRequest JsonBody<TBody>(TBody body)
    {
        var options = ResolveJsonOptions();
        _body = JsonSerializer.SerializeToUtf8Bytes(body, options);
        _contentType = "application/json";
        return this;
    }

    /// <summary>Sends <paramref name="content" /> verbatim as the request body.</summary>
    /// <param name="content">The body, encoded as UTF-8.</param>
    /// <param name="contentType">The content type to declare.</param>
    /// <returns>The request, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content" /> or <paramref name="contentType" /> is <see langword="null" />.</exception>
    public EndpointRequest Body(string content,
        string contentType)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(contentType);

        _body = Encoding.UTF8.GetBytes(content);
        _contentType = contentType;
        return this;
    }

    /// <summary>Sends <paramref name="fields" /> as an <c>application/x-www-form-urlencoded</c> body.</summary>
    /// <param name="fields">The field names and values to encode.</param>
    /// <returns>The request, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="fields" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     Exists alongside <see cref="MultipartBody" /> because a form binder must read both content
    ///     types identically — this is what exercises the one a test does not use a file in.
    /// </remarks>
    public EndpointRequest FormBody(params (string Name, string Value)[] fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var content = string.Join(
            '&',
            fields.Select(field =>
                $"{WebUtility.UrlEncode(field.Name)}={WebUtility.UrlEncode(field.Value)}"));

        return Body(content, "application/x-www-form-urlencoded");
    }

    /// <summary>Sends <paramref name="fields" /> and <paramref name="files" /> as a <c>multipart/form-data</c> body.</summary>
    /// <param name="fields">The field names and values to include as form parts.</param>
    /// <param name="files">The field name, file name and content of each uploaded file part.</param>
    /// <returns>The request, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="fields" /> or <paramref name="files" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     A fixed boundary rather than a random one, so a test asserting on a malformed body has a
    ///     stable value to reason about. The boundary declared in the <c>Content-Type</c> header and
    ///     the one written into the body's delimiter lines must agree — a mismatch there does not fail
    ///     to parse, it silently produces an empty form, which would make a "missing field" assertion
    ///     pass for the wrong reason.
    /// </remarks>
    public EndpointRequest MultipartBody(
        IEnumerable<(string Name, string Value)> fields,
        IEnumerable<(string Name, string FileName, string Content)> files)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(files);

        var builder = new StringBuilder();

        foreach (var field in fields)
        {
            builder.Append("--").Append(MultipartBoundaryValue).Append("\r\n");
            builder.Append("Content-Disposition: form-data; name=\"").Append(field.Name).Append("\"\r\n\r\n");
            builder.Append(field.Value).Append("\r\n");
        }

        foreach (var file in files)
        {
            builder.Append("--").Append(MultipartBoundaryValue).Append("\r\n");
            builder.Append("Content-Disposition: form-data; name=\"").Append(file.Name)
                   .Append("\"; filename=\"").Append(file.FileName).Append("\"\r\n");
            builder.Append("Content-Type: application/octet-stream\r\n\r\n");
            builder.Append(file.Content).Append("\r\n");
        }

        builder.Append("--").Append(MultipartBoundaryValue).Append("--\r\n");

        return Body(builder.ToString(), $"multipart/form-data; boundary={MultipartBoundaryValue}");
    }

    /// <summary>Sends the request through the endpoint.</summary>
    /// <param name="cancellationToken">The token surfaced to the endpoint as <c>RequestAborted</c>.</param>
    /// <returns>What the endpoint wrote.</returns>
    public async Task<EndpointResponse> SendAsync(CancellationToken cancellationToken = default)
    {
        // A scope per request because IHttpInvoker is registered scoped; in a host that scope comes
        // from RequestServicesContainerMiddleware, which is not part of this pipeline. An async scope
        // so a consumer's scoped IAsyncDisposable-only service disposes correctly instead of throwing
        // from a synchronous Dispose().
        await using var scope = _provider.CreateAsyncScope();

        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            RequestAborted = cancellationToken
        };

        context.Request.Method = _method;
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("localhost");
        context.Request.Path = _path;
        context.Request.QueryString = _query;

        foreach (var header in _headers)
        {
            context.Request.Headers[header.Key] = header.Value;
        }

        if (_body is not null)
        {
            context.Request.Body = new MemoryStream(_body);
            context.Request.ContentLength = _body.Length;
            context.Request.ContentType = _contentType;
        }

        var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        await _pipeline(context);

        // A 404 with no endpoint means the request never reached the endpoint under test. Returning
        // it would let a typo'd URL satisfy an assertion for the 404 the endpoint itself produces,
        // so this is the one outcome the harness refuses to hand back.
        if (context.Response.StatusCode == StatusCodes.Status404NotFound &&
            context.GetEndpoint() is null)
        {
            throw new InvalidOperationException(
                $"'{_path}{_query}' matched no route, so the request never reached the endpoint " +
                $"under test, which is mapped at {_routeDescription}. Check the URL, and remember " +
                "that a route constraint rejecting a value shows up here rather than as a binding " +
                "failure.");
        }

        return Capture(context, responseBody);
    }

    private EndpointResponse Capture(HttpContext context,
        MemoryStream responseBody)
    {
        var headers = new HeaderDictionary();
        foreach (var header in context.Response.Headers)
        {
            headers[header.Key] = header.Value;
        }

        return new EndpointResponse(
            context.Response.StatusCode,
            headers,
            context.Response.ContentType,
            responseBody.ToArray(),
            ResolveJsonOptions());
    }

    // The endpoint serializes its response with these same options, so a reader that deserializes
    // with anything else can silently disagree on naming policy or a source-generated context.
    private JsonSerializerOptions ResolveJsonOptions()
    {
        return _provider.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
    }
}
