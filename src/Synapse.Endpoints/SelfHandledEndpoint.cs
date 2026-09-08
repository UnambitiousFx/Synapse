using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Functional;
using UnambitiousFx.Functional.AspNetCore.Http;
using UnambitiousFx.Functional.AspNetCore.Mappers;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.AspNetCore.Http;
using UnambitiousFx.Synapse.Endpoints.Binding;
using UnambitiousFx.Synapse.Endpoints.Builders;
using UnambitiousFx.Synapse.Endpoints.Internal;

// UnambitiousFx.Functional declares an IResult of its own, so the unqualified name has to be
// pinned to ASP.NET's — the same device Synapse.AspNetCore's IHttpInvoker uses.
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace UnambitiousFx.Synapse.Endpoints;

/// <summary>
///     An endpoint that binds <typeparamref name="TRequest" /> and answers it itself, with no message
///     dispatched. Responds with <c>200 OK</c> and the response as the body unless configured
///     otherwise.
/// </summary>
/// <typeparam name="TRequest">The HTTP request contract. Not a message: no <c>IRequest</c> is required.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <remarks>
///     <para>
///         The tier for a route with no domain message behind it — a lookup, a health projection, a
///         lightweight read model. It keeps the generated binding, the declarative response builder and
///         the automatic OpenAPI metadata of <see cref="Endpoint{TRequest,TResponse}" />, and replaces
///         dispatch with <see cref="ExecuteAsync" />.
///     </para>
///     <para>
///         The trade-off is that nothing wraps <see cref="ExecuteAsync" />: no pipeline behaviours, so
///         no validation stage, no retries, no CQRS boundary enforcement, no outbox. When any of those
///         must apply, the endpoint has a domain message whether or not it looks like one — use
///         <see cref="Endpoint{TRequest,TResponse}" /> instead.
///     </para>
///     <para>
///         Returning <see cref="Result{TResponse}" /> rather than the value is what keeps the two tiers
///         answering identically: a failure returned here is written by the same registered
///         <c>IFailureHttpMapper</c> that maps a handler's failure, so the same failure produces the
///         same status and body on either tier.
///     </para>
///     <para>
///         Endpoints are stateless singletons: one instance is created at startup, <c>Configure</c>
///         runs once, and the same instance serves every request. Constructor injection is therefore
///         unavailable by design — resolve what you need from the <see cref="HttpContext" /> passed to
///         <see cref="ExecuteAsync" /> (<c>context.Service&lt;T&gt;()</c>).
///     </para>
/// </remarks>
public abstract class SelfHandledEndpoint<TRequest, TResponse> : BoundEndpoint<TRequest>
    where TResponse : notnull
{
    private EndpointConfiguration<TResponse>? _configuration;

    /// <summary>Configures the endpoint. Called once at startup.</summary>
    /// <param name="builder">The endpoint builder.</param>
    public virtual void Configure(IEndpointBuilder<TResponse> builder)
    {
    }

    /// <summary>Binds the request onto the contract <see cref="ExecuteAsync" /> answers.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>The bound request, or the failures preventing it.</returns>
    /// <remarks>
    ///     Implemented by the generated partial, so an endpoint the analyzer never saw does not
    ///     compile: the binding is an <c>override</c> the compiler requires rather than a binder looked
    ///     up at startup.
    /// </remarks>
    public abstract ValueTask<BindResult<TRequest>> BindAsync(HttpContext context);

    /// <summary>Answers the bound request.</summary>
    /// <param name="request">The bound request.</param>
    /// <param name="context">The HTTP context, and the way to reach request-scoped services.</param>
    /// <param name="cancellationToken">Cancellation token, tied to the request.</param>
    /// <returns>The response, or the failure to map.</returns>
    /// <remarks>
    ///     Named <c>ExecuteAsync</c> rather than <c>HandleAsync</c> because <c>HandleAsync</c> is
    ///     already taken, and sealed, by <see cref="BoundEndpoint{TBound}" />: overloading it here
    ///     would offer an author two same-named members of which only one can be overridden.
    /// </remarks>
    public abstract ValueTask<Result<TResponse>> ExecuteAsync(TRequest request,
        HttpContext context,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Maps a successful response to an HTTP result. Override for full control; prefer the
    ///     declarative methods on <see cref="IEndpointBuilder{TResponse}" /> where they suffice,
    ///     because those also produce accurate OpenAPI metadata.
    /// </summary>
    /// <param name="response">The response <see cref="ExecuteAsync" /> returned.</param>
    /// <param name="context">The HTTP context.</param>
    /// <returns>The HTTP result to write.</returns>
    public virtual IResult OnSuccess(TResponse response,
        HttpContext context)
    {
        return TypedResults.Ok(response);
    }

    /// <summary>Not used at this level; configure through the typed overload instead.</summary>
    /// <param name="builder">Unused.</param>
    /// <remarks>See <see cref="RawEndpoint{TRequest,TResponse}.Configure(IRawEndpointBuilder)" />.</remarks>
    public sealed override void Configure(IRawEndpointBuilder builder)
    {
    }

    /// <inheritdoc />
    private protected sealed override ValueTask<BindResult<TRequest>> BindBoundAsync(HttpContext context)
    {
        return BindAsync(context);
    }

    /// <inheritdoc />
    private protected sealed override async ValueTask<IResult> ProduceResultAsync(TRequest bound,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var configuration = Mapped(_configuration);
        var result = await ExecuteAsync(bound, context, cancellationToken);

        if (result.TryGet(out var response, out _))
        {
            return configuration.SuccessMapper is not null
                ? configuration.SuccessMapper(response!)
                : OnSuccess(response!, context);
        }

        // The same mapper HttpInvoker resolves, applied the same way it applies it, so a failure
        // returned here and the identical failure returned by a handler answer identically. Resolved
        // rather than dispatched through IHttpInvoker because there is no message to invoke.
        return await result.AsHttpBuilder(context.RequestServices.GetRequiredService<IFailureHttpMapper>());
    }

    internal sealed override RawEndpointPlan CreatePlan(EndpointMetadata metadata)
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

                // Declared only when the configured mapper writes a body — see docs/known-issues/054.
                handlerBuilder.WithMetadata(new ProducesResponseMetadata(
                    SuccessStatusCode(configuration),
                    configuration.SuccessResponseHasBody ? typeof(TResponse) : null));
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

/// <summary>
///     An endpoint that binds <typeparamref name="TRequest" /> and answers it itself, with no message
///     dispatched and no response body. Responds with <c>204 No Content</c> unless configured
///     otherwise.
/// </summary>
/// <typeparam name="TRequest">The HTTP request contract. Not a message: no <c>IRequest</c> is required.</typeparam>
/// <remarks>
///     See <see cref="SelfHandledEndpoint{TRequest,TResponse}" />; this is the same tier for the arity
///     with no response body, including the trade-off that nothing wraps <see cref="ExecuteAsync" />.
/// </remarks>
public abstract class SelfHandledEndpoint<TRequest> : BoundEndpoint<TRequest>
{
    private EndpointConfiguration<Unit>? _configuration;

    /// <summary>Configures the endpoint. Called once at startup.</summary>
    /// <param name="builder">The endpoint builder.</param>
    public virtual void Configure(IEndpointBuilder builder)
    {
    }

    /// <summary>Binds the request onto the contract <see cref="ExecuteAsync" /> answers.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>The bound request, or the failures preventing it.</returns>
    /// <remarks>See <see cref="SelfHandledEndpoint{TRequest,TResponse}.BindAsync" />.</remarks>
    public abstract ValueTask<BindResult<TRequest>> BindAsync(HttpContext context);

    /// <summary>Answers the bound request.</summary>
    /// <param name="request">The bound request.</param>
    /// <param name="context">The HTTP context, and the way to reach request-scoped services.</param>
    /// <param name="cancellationToken">Cancellation token, tied to the request.</param>
    /// <returns>Success, or the failure to map.</returns>
    /// <remarks>See <see cref="SelfHandledEndpoint{TRequest,TResponse}.ExecuteAsync" /> for the name.</remarks>
    public abstract ValueTask<Result> ExecuteAsync(TRequest request,
        HttpContext context,
        CancellationToken cancellationToken);

    /// <summary>Maps a successful run to an HTTP result.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>The HTTP result to write.</returns>
    public virtual IResult OnSuccess(HttpContext context)
    {
        return TypedResults.NoContent();
    }

    /// <summary>Not used at this level; configure through the typed overload instead.</summary>
    /// <param name="builder">Unused.</param>
    /// <remarks>See <see cref="RawEndpoint{TRequest,TResponse}.Configure(IRawEndpointBuilder)" />.</remarks>
    public sealed override void Configure(IRawEndpointBuilder builder)
    {
    }

    /// <inheritdoc />
    private protected sealed override ValueTask<BindResult<TRequest>> BindBoundAsync(HttpContext context)
    {
        return BindAsync(context);
    }

    /// <inheritdoc />
    private protected sealed override async ValueTask<IResult> ProduceResultAsync(TRequest bound,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var configuration = Mapped(_configuration);
        var result = await ExecuteAsync(bound, context, cancellationToken);

        if (result.IsSuccess)
        {
            return configuration.SuccessMapper is not null
                ? configuration.SuccessMapper(default)
                : OnSuccess(context);
        }

        // The same mapper HttpInvoker resolves, applied the same way it applies it — see the
        // value arity for why this is resolved rather than dispatched.
        return await result.AsHttpBuilder(context.RequestServices.GetRequiredService<IFailureHttpMapper>());
    }

    internal sealed override RawEndpointPlan CreatePlan(EndpointMetadata metadata)
    {
        var builder = new EndpointBuilder<Unit>(metadata);
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

                handlerBuilder.WithMetadata(new ProducesResponseMetadata(SuccessStatusCode(configuration)));
                handlerBuilder.ProducesValidationProblem();
                configuration.ApplyMetadata(handlerBuilder);
            }
        };
    }

    private static int SuccessStatusCode(EndpointConfiguration<Unit> configuration)
    {
        return configuration.DeclaredSuccessStatusCode ?? StatusCodes.Status204NoContent;
    }
}


/// <summary>
///     The middle level for a command with no response: you write the binding, the base class
///     dispatches <typeparamref name="TRequest" />. Responds with <c>204 No Content</c> unless
///     configured otherwise.
/// </summary>
/// <typeparam name="TRequest">The command to dispatch.</typeparam>
/// <remarks>
///     See <see cref="RawEndpoint{TRequest,TResponse}" />; this is the same level for the arity with no
///     response body.
/// </remarks>
public abstract class RawEndpoint<TRequest> : BoundEndpoint<TRequest>
    where TRequest : IRequest
{
    private EndpointConfiguration<Unit>? _configuration;

    /// <summary>Configures the endpoint. Called once at startup.</summary>
    /// <param name="builder">The endpoint builder.</param>
    public virtual void Configure(IEndpointBuilder builder)
    {
    }

    /// <summary>Binds the request onto the command to dispatch.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>The bound command, or the failures preventing it.</returns>
    public abstract ValueTask<BindResult<TRequest>> BindAsync(HttpContext context);

    /// <summary>Maps a successful dispatch to an HTTP result.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>The HTTP result to write.</returns>
    public virtual IResult OnSuccess(HttpContext context)
    {
        return TypedResults.NoContent();
    }

    /// <summary>Not used at this level; configure through the typed overload instead.</summary>
    /// <param name="builder">Unused.</param>
    /// <remarks>See <see cref="RawEndpoint{TRequest,TResponse}.Configure(IRawEndpointBuilder)" />.</remarks>
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

        // The onSuccess factory is only invoked by the invoker when the dispatch actually
        // succeeds; a mapped failure is written as-is and never reaches it.
        return invoker.InvokeAsync(
            bound,
            () => configuration.SuccessMapper is not null
                ? configuration.SuccessMapper(default)
                : OnSuccess(context),
            cancellationToken);
    }

    internal override RawEndpointPlan CreatePlan(EndpointMetadata metadata)
    {
        var builder = new EndpointBuilder<Unit>(metadata);
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

                handlerBuilder.WithMetadata(new ProducesResponseMetadata(SuccessStatusCode(configuration)));
                handlerBuilder.ProducesValidationProblem();
                configuration.ApplyMetadata(handlerBuilder);
            }
        };
    }

    private static int SuccessStatusCode(EndpointConfiguration<Unit> configuration)
    {
        return configuration.DeclaredSuccessStatusCode ?? StatusCodes.Status204NoContent;
    }
}

