using Microsoft.AspNetCore.Http;
using UnambitiousFx.Synapse.Endpoints.Builders;
using UnambitiousFx.Synapse.Endpoints.Internal;

namespace UnambitiousFx.Synapse.Endpoints;

/// <summary>
///     The low level of the endpoint surface: you get the <see cref="HttpContext" /> and return an
///     <see cref="IResult" />. Nothing is bound, dispatched or mapped for you.
/// </summary>
/// <remarks>
///     <para>
///         Reach for this when the HTTP contract is not a message — a webhook whose payload is
///         someone else's schema, a conditional <c>GET</c> that answers <c>304</c> from an
///         <c>If-None-Match</c> header, a health check, a redirect, a file download. When the contract
///         <em>is</em> a message, use <see cref="Endpoint{TRequest,TResponse}" /> and let the generated
///         binder do the work; when only the binding is unusual, use
///         <see cref="RawEndpoint{TRequest,TResponse}" /> and hand-write <c>BindAsync</c>.
///     </para>
///     <para>
///         The helpers a handler needs are extension methods on <see cref="HttpContext" /> in
///         <c>UnambitiousFx.Synapse.Endpoints.Binding</c>: typed route, query and header readers,
///         <c>BodyAsync</c>, and <c>Validate</c> for accumulating several bad inputs into one
///         <c>400</c>. The generated binders of the high level call the very same primitives.
///     </para>
///     <para>
///         Endpoints are stateless singletons: one instance is created at startup,
///         <see cref="Configure" /> runs once, and the same instance serves every request.
///         Constructor injection is therefore unavailable by design — resolve what you need from
///         <see cref="HttpContext.RequestServices" /> (<c>context.Service&lt;T&gt;()</c>).
///     </para>
///     <para>
///         Unlike the higher tiers, a low-level endpoint declares no OpenAPI metadata automatically:
///         it has no request or response type to infer one from, and no <c>400</c> is guaranteed
///         because nothing binds. Say what you accept and produce through
///         <see cref="IRawEndpointBuilder" /> if the endpoint should appear correctly in the document.
///     </para>
/// </remarks>
public abstract class RawEndpoint : EndpointBase
{
    /// <summary>Configures the endpoint. Called once at startup.</summary>
    /// <param name="builder">The endpoint builder.</param>
    public virtual void Configure(IRawEndpointBuilder builder)
    {
    }

    /// <summary>Handles the request.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="cancellationToken">Cancellation token, tied to the request.</param>
    /// <returns>The result to write to the response.</returns>
    public abstract ValueTask<IResult> HandleAsync(HttpContext context,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Whether this endpoint should declare that it accepts a JSON request body.
    /// </summary>
    /// <param name="httpMethods">The endpoint's declared HTTP methods.</param>
    /// <returns><see langword="true" /> to declare <c>Accepts</c>.</returns>
    /// <remarks>
    ///     The verb is all this tier has to go on, and it is the right answer here: the binding is
    ///     hand-written, so the author may read a body on any verb that carries one. The tiers with a
    ///     generated binder know better and override this — see <c>docs/known-issues/067</c>.
    /// </remarks>
    private protected virtual bool DeclaresRequestBody(string[] httpMethods)
    {
        return !HttpMethodHelpers.AllVerbsAreBodyless(httpMethods);
    }

    /// <summary>
    ///     Resolves this endpoint's route, verbs and OpenAPI metadata. Called once at startup.
    /// </summary>
    /// <param name="metadata">The route metadata generated from the endpoint's attributes.</param>
    /// <returns>The resolved plan.</returns>
    /// <remarks>
    ///     The customization point for endpoint shapes that configure through a typed builder rather
    ///     than <see cref="IRawEndpointBuilder" />, and that can declare metadata from their own type
    ///     arguments. Startup-only: everything request-time goes through <see cref="HandleAsync" />.
    /// </remarks>
    internal virtual RawEndpointPlan CreatePlan(EndpointMetadata metadata)
    {
        var builder = new RawEndpointBuilder(metadata);
        Configure(builder);
        return builder.Build();
    }

    /// <summary>
    ///     Builds the descriptor used to map this endpoint.
    /// </summary>
    /// <param name="metadata">The route metadata generated from the endpoint's attributes.</param>
    /// <returns>The descriptor.</returns>
    /// <remarks>
    ///     Sealed, and the only place in the library a <see cref="EndpointDescriptor" /> is
    ///     constructed. Every endpoint shape — including the four high-level ones — therefore reaches
    ///     the route table through one <see cref="HandleAsync" /> call, so the two levels cannot drift
    ///     apart without the compiler saying so.
    /// </remarks>
    internal sealed override EndpointDescriptor CreateDescriptor(EndpointMetadata metadata)
    {
        var plan = CreatePlan(metadata);

        return new EndpointDescriptor
        {
            Route = plan.Route,
            HttpMethods = plan.HttpMethods,
            ApplyMetadata = plan.ApplyMetadata,
            InvokeAsync = async context =>
            {
                // A null result would otherwise dereference as a NullReferenceException out of the
                // request delegate: a 500 naming neither the endpoint nor the cause. HandleAsync is
                // public API of the low tier, so returning null is a mistake user code can now make —
                // see docs/known-issues/056.
                var result = await HandleAsync(context, context.RequestAborted)
                             ?? throw new InvalidOperationException(
                                 $"Endpoint '{GetType()}' returned a null result from HandleAsync. " +
                                 "Return a result instead — TypedResults.Ok(value), " +
                                 "TypedResults.NoContent(), or Results.Empty to write nothing.");

                await result.ExecuteAsync(context);
            }
        };
    }
}
