using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace UnambitiousFx.Synapse.Endpoints.Testing;

/// <summary>
///     One request to send through a harness. Build it fluently, then <see cref="SendAsync" />.
/// </summary>
public sealed class EndpointRequest
{
    private readonly IServiceProvider _provider;
    private readonly RequestDelegate _pipeline;
    private readonly string _routeDescription;
    private readonly string _method;
    private readonly string _path;
    private QueryString _query;

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

    /// <summary>Sends the request through the endpoint.</summary>
    /// <param name="cancellationToken">The token surfaced to the endpoint as <c>RequestAborted</c>.</param>
    /// <returns>What the endpoint wrote.</returns>
    public async Task<EndpointResponse> SendAsync(CancellationToken cancellationToken = default)
    {
        // A scope per request because IHttpInvoker is registered scoped; in a host that scope comes
        // from RequestServicesContainerMiddleware, which is not part of this pipeline.
        using var scope = _provider.CreateScope();

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

        var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        await _pipeline(context);

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
            responseBody.ToArray());
    }
}
