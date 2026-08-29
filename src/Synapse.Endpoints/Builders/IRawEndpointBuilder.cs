using Microsoft.AspNetCore.Builder;

namespace UnambitiousFx.Synapse.Endpoints.Builders;

/// <summary>
///     Configures a low-level endpoint. Route and verb normally come from the endpoint's attribute;
///     the verb methods here exist for endpoints whose route must be computed.
/// </summary>
/// <remarks>
///     <para>
///         Deliberately narrower than <see cref="IEndpointBuilder" /> in one direction and wider in
///         another. It omits <c>NoContent</c> and <c>StatusCode</c>, which set a success mapper that a
///         low-level endpoint never consults — it returns its own result — so offering them would be a
///         trap rather than a convenience.
///     </para>
///     <para>
///         It adds <see cref="Accepts{TRequest}" /> and the <c>Produces</c> overloads because nothing
///         about a hand-written handler's contract can be inferred. A high-level endpoint's request and
///         response types are its base class's type arguments; a low-level endpoint has none, so an
///         endpoint that wants to appear correctly in an OpenAPI document has to say what it accepts
///         and produces. Declaring nothing is valid and emits nothing.
///     </para>
/// </remarks>
public interface IRawEndpointBuilder
{
    /// <summary>Declares the route and HTTP method.</summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="template">The route template.</param>
    /// <returns>The builder, for chaining.</returns>
    IRawEndpointBuilder Route(string method, string template);

    /// <summary>Declares a <c>GET</c> route.</summary>
    /// <param name="template">The route template.</param>
    /// <returns>The builder, for chaining.</returns>
    IRawEndpointBuilder Get(string template);

    /// <summary>Declares a <c>POST</c> route.</summary>
    /// <param name="template">The route template.</param>
    /// <returns>The builder, for chaining.</returns>
    IRawEndpointBuilder Post(string template);

    /// <summary>Declares a <c>PUT</c> route.</summary>
    /// <param name="template">The route template.</param>
    /// <returns>The builder, for chaining.</returns>
    IRawEndpointBuilder Put(string template);

    /// <summary>Declares a <c>PATCH</c> route.</summary>
    /// <param name="template">The route template.</param>
    /// <returns>The builder, for chaining.</returns>
    IRawEndpointBuilder Patch(string template);

    /// <summary>Declares a <c>DELETE</c> route.</summary>
    /// <param name="template">The route template.</param>
    /// <returns>The builder, for chaining.</returns>
    IRawEndpointBuilder Delete(string template);

    /// <summary>Adds OpenAPI tags.</summary>
    /// <param name="tags">The tags.</param>
    /// <returns>The builder, for chaining.</returns>
    IRawEndpointBuilder Tag(params string[] tags);

    /// <summary>Sets the OpenAPI summary.</summary>
    /// <param name="summary">The summary.</param>
    /// <returns>The builder, for chaining.</returns>
    IRawEndpointBuilder Summary(string summary);

    /// <summary>Sets the OpenAPI description.</summary>
    /// <param name="description">The description.</param>
    /// <returns>The builder, for chaining.</returns>
    IRawEndpointBuilder Description(string description);

    /// <summary>Sets the endpoint name used for link generation.</summary>
    /// <param name="name">The endpoint name.</param>
    /// <returns>The builder, for chaining.</returns>
    IRawEndpointBuilder Name(string name);

    /// <summary>Requires authorization, optionally against named policies.</summary>
    /// <param name="policies">The policy names, or none for the default policy.</param>
    /// <returns>The builder, for chaining.</returns>
    IRawEndpointBuilder RequireAuthorization(params string[] policies);

    /// <summary>Allows anonymous access, overriding a group's requirement.</summary>
    /// <returns>The builder, for chaining.</returns>
    IRawEndpointBuilder AllowAnonymous();

    /// <summary>
    ///     Declares the request body type this endpoint reads, for OpenAPI and for the consumes
    ///     matcher policy that rejects a mismatched <c>Content-Type</c> with <c>415</c>.
    /// </summary>
    /// <typeparam name="TRequest">The request body type.</typeparam>
    /// <param name="contentType">The content type.</param>
    /// <returns>The builder, for chaining.</returns>
    IRawEndpointBuilder Accepts<TRequest>(string contentType = "application/json")
        where TRequest : notnull;

    /// <summary>Declares a response with no body.</summary>
    /// <param name="statusCode">The status code.</param>
    /// <returns>The builder, for chaining.</returns>
    IRawEndpointBuilder Produces(int statusCode);

    /// <summary>Declares a response with a typed body.</summary>
    /// <typeparam name="TResponse">The response body type.</typeparam>
    /// <param name="statusCode">The status code.</param>
    /// <param name="contentType">The content type.</param>
    /// <returns>The builder, for chaining.</returns>
    IRawEndpointBuilder Produces<TResponse>(int statusCode = 200, string contentType = "application/json")
        where TResponse : notnull;

    /// <summary>
    ///     Escape hatch onto the underlying <see cref="RouteHandlerBuilder" />, for endpoint
    ///     filters, rate limiting, output caching, versioning and anything else this surface does
    ///     not wrap.
    /// </summary>
    /// <param name="configure">The configuration callback, applied at startup.</param>
    /// <returns>The builder, for chaining.</returns>
    IRawEndpointBuilder Raw(Action<RouteHandlerBuilder> configure);
}
