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
///         differs from this class in exactly one respect: it takes its binder from the generated
///         registry instead of asking you for one.
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
public abstract class RawEndpoint<TRequest, TResponse> : RawEndpoint
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
    /// <remarks>
    ///     Sealed: bind, dispatch and map is the contract of this level. Change the binding through
    ///     <see cref="BindAsync" />, the response through <see cref="OnSuccess" /> or the builder.
    /// </remarks>
    public sealed override async ValueTask<IResult> HandleAsync(HttpContext context,
        CancellationToken cancellationToken)
    {
        var bound = await BindAsync(context);
        if (!bound.IsSuccess)
        {
            return bound.Problem();
        }

        var configuration = Mapped(_configuration);
        var invoker = context.RequestServices.GetRequiredService<IHttpInvoker>();

        // Failures flow through the registered IFailureHttpMapper, unchanged.
        return await invoker.InvokeAsync(
            bound.Value!,
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

        return new RawEndpointPlan
        {
            Route = configuration.Route,
            HttpMethods = configuration.HttpMethods,
            ApplyMetadata = handlerBuilder =>
            {
                // Declared explicitly because a RequestDelegate-shaped endpoint infers nothing.
                if (!HttpMethodHelpers.AllVerbsAreBodyless(configuration.HttpMethods))
                {
                    handlerBuilder.Accepts<TRequest>("application/json");
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
