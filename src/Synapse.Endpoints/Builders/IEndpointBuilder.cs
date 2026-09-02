using Microsoft.AspNetCore.Builder;

namespace UnambitiousFx.Synapse.Endpoints.Builders;

/// <summary>
///     Configures one endpoint. Route and verb normally come from the endpoint's attribute; the
///     verb methods here exist for endpoints whose route must be computed.
/// </summary>
public interface IEndpointBuilder
{
    /// <summary>Declares the route and HTTP method.</summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="template">The route template.</param>
    /// <returns>The builder, for chaining.</returns>
    IEndpointBuilder Route(string method, string template);

    /// <summary>Declares a <c>GET</c> route.</summary>
    /// <param name="template">The route template.</param>
    /// <returns>The builder, for chaining.</returns>
    IEndpointBuilder Get(string template);

    /// <summary>Declares a <c>POST</c> route.</summary>
    /// <param name="template">The route template.</param>
    /// <returns>The builder, for chaining.</returns>
    IEndpointBuilder Post(string template);

    /// <summary>Declares a <c>PUT</c> route.</summary>
    /// <param name="template">The route template.</param>
    /// <returns>The builder, for chaining.</returns>
    IEndpointBuilder Put(string template);

    /// <summary>Declares a <c>PATCH</c> route.</summary>
    /// <param name="template">The route template.</param>
    /// <returns>The builder, for chaining.</returns>
    IEndpointBuilder Patch(string template);

    /// <summary>Declares a <c>DELETE</c> route.</summary>
    /// <param name="template">The route template.</param>
    /// <returns>The builder, for chaining.</returns>
    IEndpointBuilder Delete(string template);

    /// <summary>Adds OpenAPI tags.</summary>
    /// <param name="tags">The tags.</param>
    /// <returns>The builder, for chaining.</returns>
    IEndpointBuilder Tag(params string[] tags);

    /// <summary>Sets the OpenAPI summary.</summary>
    /// <param name="summary">The summary.</param>
    /// <returns>The builder, for chaining.</returns>
    IEndpointBuilder Summary(string summary);

    /// <summary>Sets the OpenAPI description.</summary>
    /// <param name="description">The description.</param>
    /// <returns>The builder, for chaining.</returns>
    IEndpointBuilder Description(string description);

    /// <summary>Sets the endpoint name used for link generation.</summary>
    /// <param name="name">The endpoint name.</param>
    /// <returns>The builder, for chaining.</returns>
    IEndpointBuilder Name(string name);

    /// <summary>Requires authorization, optionally against named policies.</summary>
    /// <param name="policies">The policy names, or none for the default policy.</param>
    /// <returns>The builder, for chaining.</returns>
    IEndpointBuilder RequireAuthorization(params string[] policies);

    /// <summary>Allows anonymous access, overriding a group's requirement.</summary>
    /// <returns>The builder, for chaining.</returns>
    IEndpointBuilder AllowAnonymous();

    /// <summary>Responds with <c>204 No Content</c> on success.</summary>
    /// <returns>The builder, for chaining.</returns>
    IEndpointBuilder NoContent();

    /// <summary>Responds with a fixed status code and no body on success.</summary>
    /// <param name="statusCode">The status code.</param>
    /// <returns>The builder, for chaining.</returns>
    IEndpointBuilder StatusCode(int statusCode);

    /// <summary>Declares an additional response with no body, for OpenAPI.</summary>
    /// <param name="statusCode">The status code.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <remarks>
    ///     Only two outcomes are declared for you: the configured success status, and the <c>400</c>
    ///     validation problem a binding failure sends. Every other status the endpoint really produces
    ///     — the <c>401</c>, <c>404</c>, <c>409</c> or <c>500</c> the registered
    ///     <c>IFailureHttpMapper</c> writes when the dispatched message returns a failed <c>Result</c>
    ///     — cannot be inferred from anything the endpoint declares, so until it is named here the
    ///     published document is not merely incomplete but wrong about what the endpoint can return,
    ///     and a generated client has no error model at all.
    /// </remarks>
    IEndpointBuilder Produces(int statusCode);

    /// <summary>Declares an additional response with a typed body, for OpenAPI.</summary>
    /// <typeparam name="TBody">The response body type.</typeparam>
    /// <param name="statusCode">The status code.</param>
    /// <param name="contentType">The content type.</param>
    /// <returns>The builder, for chaining.</returns>
    IEndpointBuilder Produces<TBody>(int statusCode, string contentType = "application/json")
        where TBody : notnull;

    /// <summary>Declares an additional <c>ProblemDetails</c> response, for OpenAPI.</summary>
    /// <param name="statusCode">The status code.</param>
    /// <returns>The builder, for chaining.</returns>
    IEndpointBuilder ProducesProblem(int statusCode);

    /// <summary>
    ///     Declares an additional <c>HttpValidationProblemDetails</c> response — a problem document
    ///     plus its <c>errors</c> dictionary — for OpenAPI.
    /// </summary>
    /// <param name="statusCode">The status code.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <remarks>
    ///     The <c>400</c> the default declares is already emitted by every tier, so the useful call is
    ///     a second validation status such as <c>422</c>.
    /// </remarks>
    IEndpointBuilder ProducesValidationProblem(int statusCode = 400);

    /// <summary>
    ///     Escape hatch onto the underlying <see cref="RouteHandlerBuilder" />, for endpoint
    ///     filters, rate limiting, output caching, versioning and anything else this surface does
    ///     not wrap.
    /// </summary>
    /// <param name="configure">The configuration callback, applied at startup.</param>
    /// <returns>The builder, for chaining.</returns>
    IEndpointBuilder Raw(Action<RouteHandlerBuilder> configure);
}
