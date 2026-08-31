using Microsoft.AspNetCore.Builder;

namespace UnambitiousFx.Synapse.Endpoints.Builders;

/// <summary>
///     Configures a streaming endpoint. Route and verb normally come from the endpoint's attribute;
///     the verb methods here exist for endpoints whose route must be computed.
/// </summary>
/// <remarks>
///     <para>
///         Deliberately narrower than <see cref="IEndpointBuilder" />: it omits <c>NoContent</c> and
///         <c>StatusCode</c>, which set a success mapper a streaming endpoint never consults. A stream's
///         status line is committed before the first item is produced, and its body is the negotiated
///         sequence, so neither method can mean anything here — offering them would be a trap rather
///         than a convenience, exactly as it would be on <see cref="IRawEndpointBuilder" />.
///     </para>
///     <para>
///         Nothing else is lost: the response of a streaming endpoint is already declared from its type
///         arguments (<c>200</c> with <c>IAsyncEnumerable&lt;TItem&gt;</c> for both
///         <c>application/json</c> and <c>text/event-stream</c>), and anything further goes through
///         <see cref="Raw" />.
///     </para>
/// </remarks>
public interface IStreamEndpointBuilder
{
    /// <summary>Declares the route and HTTP method.</summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="template">The route template.</param>
    /// <returns>The builder, for chaining.</returns>
    IStreamEndpointBuilder Route(string method, string template);

    /// <summary>Declares a <c>GET</c> route.</summary>
    /// <param name="template">The route template.</param>
    /// <returns>The builder, for chaining.</returns>
    IStreamEndpointBuilder Get(string template);

    /// <summary>Declares a <c>POST</c> route, for a stream whose request carries a body.</summary>
    /// <param name="template">The route template.</param>
    /// <returns>The builder, for chaining.</returns>
    IStreamEndpointBuilder Post(string template);

    /// <summary>Adds OpenAPI tags.</summary>
    /// <param name="tags">The tags.</param>
    /// <returns>The builder, for chaining.</returns>
    IStreamEndpointBuilder Tag(params string[] tags);

    /// <summary>Sets the OpenAPI summary.</summary>
    /// <param name="summary">The summary.</param>
    /// <returns>The builder, for chaining.</returns>
    IStreamEndpointBuilder Summary(string summary);

    /// <summary>Sets the OpenAPI description.</summary>
    /// <param name="description">The description.</param>
    /// <returns>The builder, for chaining.</returns>
    IStreamEndpointBuilder Description(string description);

    /// <summary>Sets the endpoint name used for link generation.</summary>
    /// <param name="name">The endpoint name.</param>
    /// <returns>The builder, for chaining.</returns>
    IStreamEndpointBuilder Name(string name);

    /// <summary>Requires authorization, optionally against named policies.</summary>
    /// <param name="policies">The policy names, or none for the default policy.</param>
    /// <returns>The builder, for chaining.</returns>
    IStreamEndpointBuilder RequireAuthorization(params string[] policies);

    /// <summary>Allows unauthenticated requests, overriding an inherited policy.</summary>
    /// <returns>The builder, for chaining.</returns>
    IStreamEndpointBuilder AllowAnonymous();

    /// <summary>
    ///     Escape hatch onto the underlying <see cref="RouteHandlerBuilder" />, for filters, caching,
    ///     rate limiting, additional <c>Produces</c> declarations, or anything else not wrapped here.
    /// </summary>
    /// <param name="configure">The callback.</param>
    /// <returns>The builder, for chaining.</returns>
    IStreamEndpointBuilder Raw(Action<RouteHandlerBuilder> configure);
}
