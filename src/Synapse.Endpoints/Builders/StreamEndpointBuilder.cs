using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using UnambitiousFx.Synapse.Endpoints.Internal;

namespace UnambitiousFx.Synapse.Endpoints.Builders;

/// <summary>Default <see cref="IStreamEndpointBuilder" /> implementation.</summary>
/// <remarks>
///     Backed by the same <see cref="EndpointBuilderCore" /> as every other builder, so the
///     "route from the attribute, else from <c>Configure</c>, else throw" rule stays in one place.
///     It resolves to a <see cref="RawEndpointPlan" /> rather than an
///     <c>EndpointConfiguration&lt;TResponse&gt;</c> because a streaming endpoint has no success
///     mapper to carry — which is the whole reason this builder exists separately.
/// </remarks>
internal sealed class StreamEndpointBuilder : IStreamEndpointBuilder
{
    private readonly EndpointBuilderCore _core;

    internal StreamEndpointBuilder(EndpointMetadata declared)
    {
        _core = new EndpointBuilderCore(declared);
    }

    /// <inheritdoc />
    public IStreamEndpointBuilder Route(string method,
        string template)
    {
        _core.Route(method, template);
        return this;
    }

    /// <inheritdoc />
    public IStreamEndpointBuilder Get(string template)
    {
        return Route("GET", template);
    }

    /// <inheritdoc />
    public IStreamEndpointBuilder Post(string template)
    {
        return Route("POST", template);
    }

    /// <inheritdoc />
    public IStreamEndpointBuilder Tag(params string[] tags)
    {
        _core.AddMetadata(builder => builder.WithTags(tags));
        return this;
    }

    /// <inheritdoc />
    public IStreamEndpointBuilder Summary(string summary)
    {
        _core.AddMetadata(builder => builder.WithSummary(summary));
        return this;
    }

    /// <inheritdoc />
    public IStreamEndpointBuilder Description(string description)
    {
        _core.AddMetadata(builder => builder.WithDescription(description));
        return this;
    }

    /// <inheritdoc />
    public IStreamEndpointBuilder Name(string name)
    {
        _core.AddMetadata(builder => builder.WithName(name));
        return this;
    }

    /// <inheritdoc />
    public IStreamEndpointBuilder RequireAuthorization(params string[] policies)
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
    public IStreamEndpointBuilder AllowAnonymous()
    {
        _core.AddMetadata(builder => builder.AllowAnonymous());
        return this;
    }

    /// <inheritdoc />
    public IStreamEndpointBuilder Raw(Action<RouteHandlerBuilder> configure)
    {
        _core.AddMetadata(configure);
        return this;
    }

    internal RawEndpointPlan Build()
    {
        return _core.Resolve();
    }
}
