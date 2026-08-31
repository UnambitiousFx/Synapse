using Microsoft.AspNetCore.Builder;

namespace UnambitiousFx.Synapse.Endpoints.Builders;

/// <summary>
///     Configures an endpoint that returns a response, adding response-shaping methods that need
///     the response type. Every member inherited from <see cref="IEndpointBuilder" /> is
///     re-declared here with the <see langword="new" /> keyword so that the fluent chain keeps
///     returning <see cref="IEndpointBuilder{TResponse}" /> instead of widening to the
///     non-generic <see cref="IEndpointBuilder" />.
/// </summary>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface IEndpointBuilder<TResponse> : IEndpointBuilder
{
    /// <summary>Declares the route and HTTP method.</summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="template">The route template.</param>
    /// <returns>The builder, for chaining.</returns>
    new IEndpointBuilder<TResponse> Route(string method, string template);

    /// <summary>Declares a <c>GET</c> route.</summary>
    /// <param name="template">The route template.</param>
    /// <returns>The builder, for chaining.</returns>
    new IEndpointBuilder<TResponse> Get(string template);

    /// <summary>Declares a <c>POST</c> route.</summary>
    /// <param name="template">The route template.</param>
    /// <returns>The builder, for chaining.</returns>
    new IEndpointBuilder<TResponse> Post(string template);

    /// <summary>Declares a <c>PUT</c> route.</summary>
    /// <param name="template">The route template.</param>
    /// <returns>The builder, for chaining.</returns>
    new IEndpointBuilder<TResponse> Put(string template);

    /// <summary>Declares a <c>PATCH</c> route.</summary>
    /// <param name="template">The route template.</param>
    /// <returns>The builder, for chaining.</returns>
    new IEndpointBuilder<TResponse> Patch(string template);

    /// <summary>Declares a <c>DELETE</c> route.</summary>
    /// <param name="template">The route template.</param>
    /// <returns>The builder, for chaining.</returns>
    new IEndpointBuilder<TResponse> Delete(string template);

    /// <summary>Adds OpenAPI tags.</summary>
    /// <param name="tags">The tags.</param>
    /// <returns>The builder, for chaining.</returns>
    new IEndpointBuilder<TResponse> Tag(params string[] tags);

    /// <summary>Sets the OpenAPI summary.</summary>
    /// <param name="summary">The summary.</param>
    /// <returns>The builder, for chaining.</returns>
    new IEndpointBuilder<TResponse> Summary(string summary);

    /// <summary>Sets the OpenAPI description.</summary>
    /// <param name="description">The description.</param>
    /// <returns>The builder, for chaining.</returns>
    new IEndpointBuilder<TResponse> Description(string description);

    /// <summary>Sets the endpoint name used for link generation.</summary>
    /// <param name="name">The endpoint name.</param>
    /// <returns>The builder, for chaining.</returns>
    new IEndpointBuilder<TResponse> Name(string name);

    /// <summary>Requires authorization, optionally against named policies.</summary>
    /// <param name="policies">The policy names, or none for the default policy.</param>
    /// <returns>The builder, for chaining.</returns>
    new IEndpointBuilder<TResponse> RequireAuthorization(params string[] policies);

    /// <summary>Allows anonymous access, overriding a group's requirement.</summary>
    /// <returns>The builder, for chaining.</returns>
    new IEndpointBuilder<TResponse> AllowAnonymous();

    /// <summary>Responds with <c>204 No Content</c> on success.</summary>
    /// <returns>The builder, for chaining.</returns>
    new IEndpointBuilder<TResponse> NoContent();

    /// <summary>Responds with a fixed status code and no body on success.</summary>
    /// <param name="statusCode">The status code.</param>
    /// <returns>The builder, for chaining.</returns>
    new IEndpointBuilder<TResponse> StatusCode(int statusCode);

    /// <summary>
    ///     Escape hatch onto the underlying <see cref="RouteHandlerBuilder" />, for endpoint
    ///     filters, rate limiting, output caching, versioning and anything else this surface does
    ///     not wrap.
    /// </summary>
    /// <param name="configure">The configuration callback, applied at startup.</param>
    /// <returns>The builder, for chaining.</returns>
    new IEndpointBuilder<TResponse> Raw(Action<RouteHandlerBuilder> configure);

    /// <summary>Responds with <c>200 OK</c> and the response as the body. This is the default.</summary>
    /// <returns>The builder, for chaining.</returns>
    IEndpointBuilder<TResponse> Ok();

    /// <summary>
    ///     Responds with <c>201 Created</c>, the response as the body, and a <c>Location</c>
    ///     header built from the response.
    /// </summary>
    /// <param name="location">Builds the <c>Location</c> value from the response.</param>
    /// <returns>The builder, for chaining.</returns>
    IEndpointBuilder<TResponse> Created(Func<TResponse, string> location);

    /// <summary>Responds with <c>202 Accepted</c>, optionally with a <c>Location</c> header.</summary>
    /// <param name="location">Builds the <c>Location</c> value, or <see langword="null" /> for none.</param>
    /// <returns>The builder, for chaining.</returns>
    IEndpointBuilder<TResponse> Accepted(Func<TResponse, string>? location = null);
}
