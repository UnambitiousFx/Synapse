using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace UnambitiousFx.Synapse.Endpoints.Testing;

/// <summary>
///     One mapped endpoint, ready to answer requests.
/// </summary>
/// <typeparam name="TEndpoint">The endpoint type under test.</typeparam>
/// <remarks>
///     Created by <see cref="EndpointHarness.Create{TEndpoint}()" />. Mapping happens once, in the
///     constructor, so <c>Configure</c> runs once and the endpoint instance is reused across requests
///     exactly as it is in a host.
/// </remarks>
public sealed class EndpointHarness<TEndpoint> : IDisposable
    where TEndpoint : EndpointBase, new()
{
    private readonly ServiceProvider _provider;
    private readonly RequestDelegate _pipeline;
    private readonly string _routeDescription;

    /// <summary>Initializes a new instance of the <see cref="EndpointHarness{TEndpoint}" /> class.</summary>
    /// <param name="provider">The built service provider.</param>
    /// <param name="pipeline">The built request pipeline.</param>
    /// <param name="routeDescription">The mapped route, for diagnostics.</param>
    internal EndpointHarness(ServiceProvider provider,
        RequestDelegate pipeline,
        string routeDescription)
    {
        _provider = provider;
        _pipeline = pipeline;
        _routeDescription = routeDescription;
    }

    /// <summary>Gets the services the endpoint resolves from.</summary>
    public IServiceProvider Services => _provider;

    /// <summary>Gets the route the endpoint is mapped at, as <c>GET /tasks/{taskId:guid}</c>.</summary>
    public string RouteDescription => _routeDescription;

    /// <summary>Builds a request with an arbitrary method.</summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="url">The URL, with an optional query string.</param>
    /// <returns>The request, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="method" /> or <paramref name="url" /> is <see langword="null" />.</exception>
    public EndpointRequest Request(string method,
        string url)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(url);

        return new EndpointRequest(_provider, _pipeline, _routeDescription, method, url);
    }

    /// <summary>Builds a <c>GET</c> request.</summary>
    /// <param name="url">The URL, with an optional query string.</param>
    /// <returns>The request, for further configuration.</returns>
    public EndpointRequest Get(string url)
    {
        return Request(HttpMethods.Get, url);
    }

    /// <summary>Builds a <c>POST</c> request.</summary>
    /// <param name="url">The URL, with an optional query string.</param>
    /// <returns>The request, for further configuration.</returns>
    public EndpointRequest Post(string url)
    {
        return Request(HttpMethods.Post, url);
    }

    /// <summary>Builds a <c>PUT</c> request.</summary>
    /// <param name="url">The URL, with an optional query string.</param>
    /// <returns>The request, for further configuration.</returns>
    public EndpointRequest Put(string url)
    {
        return Request(HttpMethods.Put, url);
    }

    /// <summary>Builds a <c>PATCH</c> request.</summary>
    /// <param name="url">The URL, with an optional query string.</param>
    /// <returns>The request, for further configuration.</returns>
    public EndpointRequest Patch(string url)
    {
        return Request(HttpMethods.Patch, url);
    }

    /// <summary>Builds a <c>DELETE</c> request.</summary>
    /// <param name="url">The URL, with an optional query string.</param>
    /// <returns>The request, for further configuration.</returns>
    public EndpointRequest Delete(string url)
    {
        return Request(HttpMethods.Delete, url);
    }

    /// <summary>Builds a <c>HEAD</c> request.</summary>
    /// <param name="url">The URL, with an optional query string.</param>
    /// <returns>The request, for further configuration.</returns>
    public EndpointRequest Head(string url)
    {
        return Request(HttpMethods.Head, url);
    }

    /// <summary>Builds an <c>OPTIONS</c> request.</summary>
    /// <param name="url">The URL, with an optional query string.</param>
    /// <returns>The request, for further configuration.</returns>
    public EndpointRequest Options(string url)
    {
        return Request(HttpMethods.Options, url);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _provider.Dispose();
    }
}
