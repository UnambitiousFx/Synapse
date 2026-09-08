using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.AspNetCore.Http;
using UnambitiousFx.Synapse.Endpoints.Binding;
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
///         <c>400</c>. The generated bindings of the high level call the very same primitives.
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

    /// <summary>The kind of body this endpoint's binding reads, before the verb narrows it.</summary>
    /// <remarks>
    ///     A generated binding overrides this with a compile-time constant. The default is
    ///     <see cref="RequestBodyKind.Json" /> because a hand-written <c>BindAsync</c> may read a body
    ///     on any verb that carries one.
    /// </remarks>
    protected virtual RequestBodyKind BoundBodyKind => RequestBodyKind.Json;

    /// <summary>What this endpoint declares it accepts as a request body.</summary>
    /// <param name="httpMethods">The endpoint's declared HTTP methods.</param>
    /// <returns>The kind of body to declare, or <see cref="RequestBodyKind.None" /> to declare nothing.</returns>
    /// <remarks>
    ///     Narrowing only: a bodyless verb declares nothing whatever the binding says, including the
    ///     explicit-[FromBody]-on-a-GET shape SYNE007 warns about. What the binding adds is *which*
    ///     body — a form-bound message reads one, but not a JSON one. See docs/known-issues/067.
    ///     The verbs are resolved at startup, not compile time, because an endpoint may declare its
    ///     route (and therefore its verb) inside Configure — the shape SYNE014 reports.
    /// </remarks>
    private protected RequestBodyKind DeclaredRequestBody(string[] httpMethods)
    {
        return HttpMethodHelpers.AllVerbsAreBodyless(httpMethods)
            ? RequestBodyKind.None
            : BoundBodyKind;
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

/// <summary>
///     The middle level: you write the binding, the base class dispatches
///     <typeparamref name="TRequest" /> and maps the result. Responds with <c>200 OK</c> and the
///     response as the body unless configured otherwise.
/// </summary>
/// <typeparam name="TRequest">The command or query to dispatch.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <remarks>
///     <para>
///         Use this when the request maps onto a message but the mapping is not one the five binding
///         conventions can express — a header that has to be split, a legacy query-string shape, a
///         value that needs normalising before it becomes part of the message. Everything downstream of
///         <see cref="BindAsync" /> is identical to <see cref="Endpoint{TRequest,TResponse}" />, which
///         differs from this class in exactly one respect: the analyzer writes its
///         <see cref="BindAsync" /> for it instead of asking you for one.
///     </para>
///     <para>
///         Read the request with the extension methods in
///         <c>UnambitiousFx.Synapse.Endpoints.Binding</c>, and prefer
///         <c>context.Validate()</c> so several bad inputs produce one <c>400</c> listing all of them.
///     </para>
///     <para>
///         Endpoints are stateless singletons: one instance is created at startup,
///         <c>Configure</c> runs once, and the same instance serves every request.
///         Constructor injection is therefore unavailable by design — resolve what you need from the
///         <see cref="HttpContext" />.
///     </para>
/// </remarks>
public abstract class RawEndpoint<TRequest, TResponse> : BoundEndpoint<TRequest>
    where TRequest : IRequest<TResponse>
    where TResponse : notnull
{
    private EndpointConfiguration<TResponse>? _configuration;

    /// <summary>Configures the endpoint. Called once at startup.</summary>
    /// <param name="builder">The endpoint builder.</param>
    public virtual void Configure(IEndpointBuilder<TResponse> builder)
    {
    }

    /// <summary>Binds the request onto the message to dispatch.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>The bound message, or the failures preventing it.</returns>
    /// <remarks>
    ///     A failure short-circuits to a <c>400</c> carrying every collected error and the message is
    ///     never dispatched.
    /// </remarks>
    public abstract ValueTask<BindResult<TRequest>> BindAsync(HttpContext context);

    /// <summary>
    ///     Maps a successful response to an HTTP result. Override for full control; prefer the
    ///     declarative methods on <see cref="IEndpointBuilder{TResponse}" /> where they suffice,
    ///     because those also produce accurate OpenAPI metadata.
    /// </summary>
    /// <param name="response">The handler's response.</param>
    /// <param name="context">The HTTP context.</param>
    /// <returns>The HTTP result to write.</returns>
    public virtual IResult OnSuccess(TResponse response,
        HttpContext context)
    {
        return TypedResults.Ok(response);
    }

    /// <summary>Not used at this level; configure through the typed overload instead.</summary>
    /// <param name="builder">Unused.</param>
    /// <remarks>
    ///     Sealed deliberately. This level configures through
    ///     <see cref="Configure(IEndpointBuilder{TResponse})" />, so leaving the low-level overload
    ///     open would let a subclass override a hook that is never called and wonder why its
    ///     configuration is ignored. Sealing turns that into a compile error.
    /// </remarks>
    public sealed override void Configure(IRawEndpointBuilder builder)
    {
    }

    /// <inheritdoc />
    private protected sealed override ValueTask<BindResult<TRequest>> BindBoundAsync(HttpContext context)
    {
        return BindAsync(context);
    }

    /// <inheritdoc />
    private protected sealed override ValueTask<IResult> ProduceResultAsync(TRequest bound,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var configuration = Mapped(_configuration);
        var invoker = context.RequestServices.GetRequiredService<IHttpInvoker>();

        // Failures flow through the registered IFailureHttpMapper, unchanged.
        return invoker.InvokeAsync(
            bound,
            response => configuration.SuccessMapper is not null
                ? configuration.SuccessMapper(response)
                : OnSuccess(response, context),
            cancellationToken);
    }

    internal override RawEndpointPlan CreatePlan(EndpointMetadata metadata)
    {
        var builder = new EndpointBuilder<TResponse>(metadata);
        Configure(builder);
        var configuration = builder.Build();
        _configuration = configuration;
        ConfiguredProcessors = configuration.Processors;

        return new RawEndpointPlan
        {
            Route = configuration.Route,
            HttpMethods = configuration.HttpMethods,
            Processors = configuration.Processors,
            ApplyMetadata = handlerBuilder =>
            {
                // Declared explicitly because a RequestDelegate-shaped endpoint infers nothing.
                RequestBodyMetadata.Apply(handlerBuilder, DeclaredRequestBody(configuration.HttpMethods),
                    typeof(TRequest), DeclaredFormFields());

                // Attached as one entry so a single GetMetadata call retrieves the whole list —
                // see BoundParametersMetadata's remarks.
                if (DeclaredParameters() is { Count: > 0 } parameters)
                {
                    handlerBuilder.WithMetadata(new BoundParametersMetadata(parameters));
                }

                // The response type is declared only when the configured mapper actually writes one.
                // NoContent() and StatusCode(int) write a status line and nothing else, so declaring
                // typeof(TResponse) there promised a JSON body that never arrives — see
                // docs/known-issues/054.
                handlerBuilder.WithMetadata(new ProducesResponseMetadata(
                    SuccessStatusCode(configuration),
                    configuration.SuccessResponseHasBody ? typeof(TResponse) : null));

                // A validation problem, not a plain one: binding failures answer with
                // HttpValidationProblemDetails and its errors dictionary, so ProducesProblem would
                // describe a narrower body than the endpoint sends — see docs/known-issues/055.
                handlerBuilder.ProducesValidationProblem();
                configuration.ApplyMetadata(handlerBuilder);
            }
        };
    }

    private static int SuccessStatusCode(EndpointConfiguration<TResponse> configuration)
    {
        return configuration.DeclaredSuccessStatusCode ?? StatusCodes.Status200OK;
    }
}
