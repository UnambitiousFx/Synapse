using Microsoft.AspNetCore.Builder;
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
            }
        };
    }
}
