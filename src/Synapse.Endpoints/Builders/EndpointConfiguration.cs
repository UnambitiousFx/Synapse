using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace UnambitiousFx.Synapse.Endpoints.Builders;

/// <summary>Resolved configuration for one endpoint, produced once at startup.</summary>
/// <typeparam name="TResponse">The response type, or <c>Unit</c> for endpoints with no response.</typeparam>
internal sealed class EndpointConfiguration<TResponse>
{
    /// <summary>Gets the resolved route template.</summary>
    public required string Route { get; init; }

    /// <summary>Gets the resolved HTTP methods.</summary>
    public required string[] HttpMethods { get; init; }

    /// <summary>Gets the declarative success mapper, or null to fall through to <c>OnSuccess</c>.</summary>
    public Func<TResponse, IResult>? SuccessMapper { get; init; }

    /// <summary>
    ///     Gets the HTTP status code declared by the success-setting method the endpoint configured
    ///     (for example <c>Created</c> declares <c>201</c>), or <see langword="null" /> when none was
    ///     configured. Read by the endpoint base to declare accurate OpenAPI metadata instead of
    ///     always assuming <c>200 OK</c>.
    /// </summary>
    public int? DeclaredSuccessStatusCode { get; init; }

    /// <summary>Gets the callback that applies accumulated metadata to the route handler builder.</summary>
    public required Action<RouteHandlerBuilder> ApplyMetadata { get; init; }
}

/// <summary>Stand-in response type for endpoints that return nothing.</summary>
internal readonly struct Unit
{
}
