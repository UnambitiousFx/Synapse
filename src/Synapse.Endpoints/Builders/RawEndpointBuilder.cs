using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using UnambitiousFx.Synapse.Endpoints.Internal;

namespace UnambitiousFx.Synapse.Endpoints.Builders;

/// <summary>Default <see cref="IRawEndpointBuilder" /> implementation.</summary>
internal sealed class RawEndpointBuilder : IRawEndpointBuilder
{
    private readonly EndpointBuilderCore _core;

    internal RawEndpointBuilder(EndpointMetadata declared)
    {
        _core = new EndpointBuilderCore(declared);
    }

    /// <inheritdoc />
    public IRawEndpointBuilder Route(string method,
        string template)
    {
        _core.Route(method, template);
        return this;
    }

    /// <inheritdoc />
    public IRawEndpointBuilder Get(string template)
    {
        return Route("GET", template);
    }

    /// <inheritdoc />
    public IRawEndpointBuilder Post(string template)
    {
        return Route("POST", template);
    }

    /// <inheritdoc />
    public IRawEndpointBuilder Put(string template)
    {
        return Route("PUT", template);
    }

    /// <inheritdoc />
    public IRawEndpointBuilder Patch(string template)
    {
        return Route("PATCH", template);
    }

    /// <inheritdoc />
    public IRawEndpointBuilder Delete(string template)
    {
        return Route("DELETE", template);
    }

    /// <inheritdoc />
    public IRawEndpointBuilder Tag(params string[] tags)
    {
        _core.AddMetadata(builder => builder.WithTags(tags));
        return this;
    }

    /// <inheritdoc />
    public IRawEndpointBuilder Summary(string summary)
    {
        _core.AddMetadata(builder => builder.WithSummary(summary));
        return this;
    }

    /// <inheritdoc />
    public IRawEndpointBuilder Description(string description)
    {
        _core.AddMetadata(builder => builder.WithDescription(description));
        return this;
    }

    /// <inheritdoc />
    public IRawEndpointBuilder Name(string name)
    {
        _core.AddMetadata(builder => builder.WithName(name));
        return this;
    }

    /// <inheritdoc />
    public IRawEndpointBuilder RequireAuthorization(params string[] policies)
    {
        _core.AddMetadata(builder =>
        {
            if (policies.Length == 0)
            {
                builder.RequireAuthorization();
            }
            else
            {
                builder.RequireAuthorization(policies);
            }
        });
        return this;
    }

    /// <inheritdoc />
    public IRawEndpointBuilder AllowAnonymous()
    {
        _core.AddMetadata(builder => builder.AllowAnonymous());
        return this;
    }

    /// <inheritdoc />
    public IRawEndpointBuilder Accepts<TRequest>(string contentType = "application/json")
        where TRequest : notnull
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        _core.AddMetadata(builder => builder.Accepts<TRequest>(contentType));
        return this;
    }

    /// <inheritdoc />
    public IRawEndpointBuilder Produces(int statusCode)
    {
        _core.AddMetadata(builder => builder.WithMetadata(new ProducesResponseMetadata(statusCode)));
        return this;
    }

    /// <inheritdoc />
    public IRawEndpointBuilder Produces<TResponse>(int statusCode = 200,
        string contentType = "application/json")
        where TResponse : notnull
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        // The library's own IProducesResponseTypeMetadata, not the framework's Produces extension —
        // see ProducesResponseMetadata for why, and note that WithOpenApi is avoided entirely for AOT.
        _core.AddMetadata(builder => builder.WithMetadata(
            new ProducesResponseMetadata(statusCode, typeof(TResponse), [contentType])));
        return this;
    }

    /// <inheritdoc />
    public IRawEndpointBuilder Raw(Action<RouteHandlerBuilder> configure)
    {
        _core.AddMetadata(configure);
        return this;
    }

    internal RawEndpointPlan Build()
    {
        return _core.Resolve();
    }
}
