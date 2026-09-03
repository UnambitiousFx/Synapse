using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Synapse.Endpoints.Internal;

namespace UnambitiousFx.Synapse.Endpoints.Builders;

/// <summary>
///     The route, verb and metadata accumulation shared by every endpoint builder.
/// </summary>
/// <remarks>
///     Both <see cref="EndpointBuilder{TResponse}" /> and <see cref="RawEndpointBuilder" /> delegate
///     here rather than each keeping their own copy. In particular <see cref="Resolve" /> owns the
///     "route from the attribute, else from <c>Configure</c>, else throw" rule in exactly one place:
///     duplicating it would let the two levels disagree about what a routeless endpoint does, which
///     is the one thing this refactor exists to prevent.
/// </remarks>
internal sealed class EndpointBuilderCore
{
    private readonly List<Action<RouteHandlerBuilder>> _metadata = [];
    private readonly EndpointMetadata _declared;
    private string? _route;
    private string[]? _httpMethods;
    private List<Func<HttpContext, IEndpointPreProcessor>>? _preProcessors;
    private List<Func<HttpContext, IEndpointPostProcessor>>? _postProcessors;

    internal EndpointBuilderCore(EndpointMetadata declared)
    {
        _declared = declared;
    }

    /// <summary>Declares the route and HTTP method, overriding anything the attribute declared.</summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="template">The route template.</param>
    internal void Route(string method,
        string template)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(template);

        _httpMethods = [method.ToUpperInvariant()];
        _route = template;
    }

    /// <summary>Queues a callback to run against the route handler builder at startup.</summary>
    /// <param name="configure">The callback.</param>
    internal void AddMetadata(Action<RouteHandlerBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _metadata.Add(configure);
    }

    /// <summary>Registers a pre-processor, resolved from the request's services on each request.</summary>
    /// <typeparam name="TProcessor">The processor type.</typeparam>
    internal void PreProcessor<TProcessor>()
        where TProcessor : class, IEndpointPreProcessor
    {
        // A static lambda: the closure would otherwise capture nothing but still allocate per
        // registration, and the resolver is stored for the lifetime of the application anyway.
        (_preProcessors ??= []).Add(
            static context => context.RequestServices.GetRequiredService<TProcessor>());
    }

    /// <summary>Registers a post-processor, resolved from the request's services on each request.</summary>
    /// <typeparam name="TProcessor">The processor type.</typeparam>
    internal void PostProcessor<TProcessor>()
        where TProcessor : class, IEndpointPostProcessor
    {
        (_postProcessors ??= []).Add(
            static context => context.RequestServices.GetRequiredService<TProcessor>());
    }

    /// <summary>Declares a response with no body.</summary>
    /// <param name="statusCode">The status code.</param>
    /// <remarks>
    ///     Goes through <see cref="ProducesResponseMetadata" /> rather than the framework's
    ///     <c>Produces</c> extension so that a bodyless status is described as <c>void</c>:
    ///     Microsoft.AspNetCore.OpenApi skips an <c>IProducesResponseTypeMetadata</c> whose <c>Type</c>
    ///     is null outright, which would drop the declaration from the document entirely — see
    ///     docs/known-issues/051.
    /// </remarks>
    internal void Produces(int statusCode)
    {
        AddMetadata(builder => builder.WithMetadata(new ProducesResponseMetadata(statusCode)));
    }

    /// <summary>Declares a response with a typed body.</summary>
    /// <param name="statusCode">The status code.</param>
    /// <param name="bodyType">The response body type.</param>
    /// <param name="contentType">The content type.</param>
    internal void Produces(int statusCode,
        Type bodyType,
        string contentType)
    {
        ArgumentNullException.ThrowIfNull(bodyType);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        AddMetadata(builder => builder.WithMetadata(
            new ProducesResponseMetadata(statusCode, bodyType, [contentType])));
    }

    /// <summary>Declares a <c>ProblemDetails</c> response.</summary>
    /// <param name="statusCode">The status code.</param>
    /// <remarks>
    ///     The framework extension is used here, unlike for <see cref="Produces(int)" />: it declares a
    ///     type, so nothing is skipped, and the entry is then structurally identical to the <c>400</c>
    ///     each endpoint tier declares for itself.
    /// </remarks>
    internal void ProducesProblem(int statusCode)
    {
        AddMetadata(builder => builder.ProducesProblem(statusCode));
    }

    /// <summary>Declares an <c>HttpValidationProblemDetails</c> response.</summary>
    /// <param name="statusCode">The status code.</param>
    internal void ProducesValidationProblem(int statusCode)
    {
        AddMetadata(builder => builder.ProducesValidationProblem(statusCode));
    }

    /// <summary>Resolves the route, verbs and accumulated metadata.</summary>
    /// <returns>The resolved plan.</returns>
    /// <exception cref="InvalidOperationException">
    ///     Neither a route attribute nor a <c>Configure</c> call declared a route.
    /// </exception>
    internal RawEndpointPlan Resolve()
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

        var processors = _preProcessors is null && _postProcessors is null
            ? EndpointProcessors.Empty
            : new EndpointProcessors(
                _preProcessors?.ToArray() ?? [],
                _postProcessors?.ToArray() ?? []);

        return new RawEndpointPlan
        {
            Route = route,
            HttpMethods = methods,
            ApplyMetadata = builder =>
            {
                foreach (var action in metadata)
                {
                    action(builder);
                }
            },
            Processors = processors
        };
    }
}
