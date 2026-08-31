using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace UnambitiousFx.Synapse.Endpoints.Builders;

/// <summary>Default <see cref="IEndpointBuilder{TResponse}" /> implementation.</summary>
/// <typeparam name="TResponse">The response type.</typeparam>
internal sealed class EndpointBuilder<TResponse> : IEndpointBuilder<TResponse>
{
    private readonly EndpointBuilderCore _core;
    private Func<TResponse, IResult>? _successMapper;
    private int? _declaredSuccessStatusCode;
    private bool _successResponseHasBody = true;

    internal EndpointBuilder(EndpointMetadata declared)
    {
        _core = new EndpointBuilderCore(declared);
    }

    /// <inheritdoc cref="IEndpointBuilder{TResponse}.Route" />
    public IEndpointBuilder<TResponse> Route(string method, string template)
    {
        _core.Route(method, template);
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
        _core.AddMetadata(builder => builder.WithTags(tags));
        return this;
    }

    /// <inheritdoc cref="IEndpointBuilder{TResponse}.Summary" />
    public IEndpointBuilder<TResponse> Summary(string summary)
    {
        _core.AddMetadata(builder => builder.WithSummary(summary));
        return this;
    }

    /// <inheritdoc cref="IEndpointBuilder{TResponse}.Description" />
    public IEndpointBuilder<TResponse> Description(string description)
    {
        _core.AddMetadata(builder => builder.WithDescription(description));
        return this;
    }

    /// <inheritdoc cref="IEndpointBuilder{TResponse}.Name" />
    public IEndpointBuilder<TResponse> Name(string name)
    {
        _core.AddMetadata(builder => builder.WithName(name));
        return this;
    }

    /// <inheritdoc cref="IEndpointBuilder{TResponse}.RequireAuthorization" />
    public IEndpointBuilder<TResponse> RequireAuthorization(params string[] policies)
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

    /// <inheritdoc cref="IEndpointBuilder{TResponse}.AllowAnonymous" />
    public IEndpointBuilder<TResponse> AllowAnonymous()
    {
        _core.AddMetadata(builder => builder.AllowAnonymous());
        return this;
    }

    /// <inheritdoc cref="IEndpointBuilder{TResponse}.NoContent" />
    public IEndpointBuilder<TResponse> NoContent()
    {
        _successMapper = _ => TypedResults.NoContent();
        _declaredSuccessStatusCode = StatusCodes.Status204NoContent;
        _successResponseHasBody = false;
        return this;
    }

    /// <inheritdoc cref="IEndpointBuilder{TResponse}.StatusCode" />
    public IEndpointBuilder<TResponse> StatusCode(int statusCode)
    {
        // TypedResults.StatusCode writes the status line and nothing else, so whatever TResponse is,
        // this response has no body to declare.
        _successMapper = _ => TypedResults.StatusCode(statusCode);
        _declaredSuccessStatusCode = statusCode;
        _successResponseHasBody = false;
        return this;
    }

    /// <inheritdoc cref="IEndpointBuilder{TResponse}.Raw" />
    public IEndpointBuilder<TResponse> Raw(Action<RouteHandlerBuilder> configure)
    {
        _core.AddMetadata(configure);
        return this;
    }

    /// <inheritdoc />
    public IEndpointBuilder<TResponse> Ok()
    {
        _successMapper = value => TypedResults.Ok(value);
        _declaredSuccessStatusCode = StatusCodes.Status200OK;
        _successResponseHasBody = true;
        return this;
    }

    /// <inheritdoc />
    public IEndpointBuilder<TResponse> Created(Func<TResponse, string> location)
    {
        ArgumentNullException.ThrowIfNull(location);

        // TypedResults.Created, deliberately not CreatedAtRoute, which is RequiresUnreferencedCode.
        _successMapper = value => TypedResults.Created(location(value), value);
        _declaredSuccessStatusCode = StatusCodes.Status201Created;
        _successResponseHasBody = true;
        return this;
    }

    /// <inheritdoc />
    public IEndpointBuilder<TResponse> Accepted(Func<TResponse, string>? location = null)
    {
        _successMapper = value => location is null
            ? TypedResults.Accepted((string?)null, value)
            : TypedResults.Accepted(location(value), value);
        _declaredSuccessStatusCode = StatusCodes.Status202Accepted;
        _successResponseHasBody = true;
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
        var plan = _core.Resolve();

        return new EndpointConfiguration<TResponse>
        {
            Route = plan.Route,
            HttpMethods = plan.HttpMethods,
            SuccessMapper = _successMapper,
            DeclaredSuccessStatusCode = _declaredSuccessStatusCode,
            SuccessResponseHasBody = _successResponseHasBody,
            ApplyMetadata = plan.ApplyMetadata
        };
    }
}
