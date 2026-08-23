using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace UnambitiousFx.Synapse.Endpoints.Builders;

/// <summary>Default <see cref="IEndpointBuilder{TResponse}" /> implementation.</summary>
/// <typeparam name="TResponse">The response type.</typeparam>
internal sealed class EndpointBuilder<TResponse> : IEndpointBuilder<TResponse>
{
    private readonly List<Action<RouteHandlerBuilder>> _metadata = [];
    private readonly EndpointMetadata _declared;
    private string? _route;
    private string[]? _httpMethods;
    private Func<TResponse, IResult>? _successMapper;

    internal EndpointBuilder(EndpointMetadata declared)
    {
        _declared = declared;
    }

    /// <inheritdoc cref="IEndpointBuilder{TResponse}.Route" />
    public IEndpointBuilder<TResponse> Route(string method, string template)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(template);

        _httpMethods = [method.ToUpperInvariant()];
        _route = template;
        return this;
    }

    /// <inheritdoc cref="IEndpointBuilder{TResponse}.Get" />
    public IEndpointBuilder<TResponse> Get(string template)
    {
        return Route("GET", template);
    }

    /// <inheritdoc cref="IEndpointBuilder{TResponse}.Post" />
    public IEndpointBuilder<TResponse> Post(string template)
    {
        return Route("POST", template);
    }

    /// <inheritdoc cref="IEndpointBuilder{TResponse}.Put" />
    public IEndpointBuilder<TResponse> Put(string template)
    {
        return Route("PUT", template);
    }

    /// <inheritdoc cref="IEndpointBuilder{TResponse}.Patch" />
    public IEndpointBuilder<TResponse> Patch(string template)
    {
        return Route("PATCH", template);
    }

    /// <inheritdoc cref="IEndpointBuilder{TResponse}.Delete" />
    public IEndpointBuilder<TResponse> Delete(string template)
    {
        return Route("DELETE", template);
    }

    /// <inheritdoc cref="IEndpointBuilder{TResponse}.Tag" />
    public IEndpointBuilder<TResponse> Tag(params string[] tags)
    {
        _metadata.Add(builder => builder.WithTags(tags));
        return this;
    }

    /// <inheritdoc cref="IEndpointBuilder{TResponse}.Summary" />
    public IEndpointBuilder<TResponse> Summary(string summary)
    {
        _metadata.Add(builder => builder.WithSummary(summary));
        return this;
    }

    /// <inheritdoc cref="IEndpointBuilder{TResponse}.Description" />
    public IEndpointBuilder<TResponse> Description(string description)
    {
        _metadata.Add(builder => builder.WithDescription(description));
        return this;
    }

    /// <inheritdoc cref="IEndpointBuilder{TResponse}.Name" />
    public IEndpointBuilder<TResponse> Name(string name)
    {
        _metadata.Add(builder => builder.WithName(name));
        return this;
    }

    /// <inheritdoc cref="IEndpointBuilder{TResponse}.RequireAuthorization" />
    public IEndpointBuilder<TResponse> RequireAuthorization(params string[] policies)
    {
        _metadata.Add(builder =>
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

    /// <inheritdoc cref="IEndpointBuilder{TResponse}.AllowAnonymous" />
    public IEndpointBuilder<TResponse> AllowAnonymous()
    {
        _metadata.Add(builder => builder.AllowAnonymous());
        return this;
    }

    /// <inheritdoc cref="IEndpointBuilder{TResponse}.NoContent" />
    public IEndpointBuilder<TResponse> NoContent()
    {
        _successMapper = _ => TypedResults.NoContent();
        return this;
    }

    /// <inheritdoc cref="IEndpointBuilder{TResponse}.StatusCode" />
    public IEndpointBuilder<TResponse> StatusCode(int statusCode)
    {
        _successMapper = _ => TypedResults.StatusCode(statusCode);
        return this;
    }

    /// <inheritdoc cref="IEndpointBuilder{TResponse}.Raw" />
    public IEndpointBuilder<TResponse> Raw(Action<RouteHandlerBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _metadata.Add(configure);
        return this;
    }

    /// <inheritdoc />
    public IEndpointBuilder<TResponse> Ok()
    {
        _successMapper = value => TypedResults.Ok(value);
        return this;
    }

    /// <inheritdoc />
    public IEndpointBuilder<TResponse> Created(Func<TResponse, string> location)
    {
        ArgumentNullException.ThrowIfNull(location);

        // TypedResults.Created, deliberately not CreatedAtRoute, which is RequiresUnreferencedCode.
        _successMapper = value => TypedResults.Created(location(value), value);
        return this;
    }

    /// <inheritdoc />
    public IEndpointBuilder<TResponse> Accepted(Func<TResponse, string>? location = null)
    {
        _successMapper = value => location is null
            ? TypedResults.Accepted((string?)null, value)
            : TypedResults.Accepted(location(value), value);
        return this;
    }

    IEndpointBuilder IEndpointBuilder.Route(string method, string template)
    {
        return Route(method, template);
    }

    IEndpointBuilder IEndpointBuilder.Get(string template)
    {
        return Get(template);
    }

    IEndpointBuilder IEndpointBuilder.Post(string template)
    {
        return Post(template);
    }

    IEndpointBuilder IEndpointBuilder.Put(string template)
    {
        return Put(template);
    }

    IEndpointBuilder IEndpointBuilder.Patch(string template)
    {
        return Patch(template);
    }

    IEndpointBuilder IEndpointBuilder.Delete(string template)
    {
        return Delete(template);
    }

    IEndpointBuilder IEndpointBuilder.Tag(params string[] tags)
    {
        return Tag(tags);
    }

    IEndpointBuilder IEndpointBuilder.Summary(string summary)
    {
        return Summary(summary);
    }

    IEndpointBuilder IEndpointBuilder.Description(string description)
    {
        return Description(description);
    }

    IEndpointBuilder IEndpointBuilder.Name(string name)
    {
        return Name(name);
    }

    IEndpointBuilder IEndpointBuilder.RequireAuthorization(params string[] policies)
    {
        return RequireAuthorization(policies);
    }

    IEndpointBuilder IEndpointBuilder.AllowAnonymous()
    {
        return AllowAnonymous();
    }

    IEndpointBuilder IEndpointBuilder.NoContent()
    {
        return NoContent();
    }

    IEndpointBuilder IEndpointBuilder.StatusCode(int statusCode)
    {
        return StatusCode(statusCode);
    }

    IEndpointBuilder IEndpointBuilder.Raw(Action<RouteHandlerBuilder> configure)
    {
        return Raw(configure);
    }

    internal EndpointConfiguration<TResponse> Build()
    {
        var route = _route ?? (_declared.IsRouteDeclaredInConfigure ? null : _declared.Route);
        var methods = _httpMethods ?? (_declared.HttpMethods.Length > 0 ? _declared.HttpMethods : null);

        if (route is null || methods is null)
        {
            throw new InvalidOperationException(
                "The endpoint declares no route. Add a route attribute such as [Get(\"/things\")] to " +
                "the endpoint class, or declare one in Configure with builder.Get(\"/things\").");
        }

        var metadata = _metadata.ToArray();

        return new EndpointConfiguration<TResponse>
        {
            Route = route,
            HttpMethods = methods,
            SuccessMapper = _successMapper,
            ApplyMetadata = builder =>
            {
                foreach (var action in metadata)
                {
                    action(builder);
                }
            }
        };
    }
}
