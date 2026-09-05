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
///     An endpoint whose HTTP contract is separate from its CQRS message, so the wire format can
///     evolve independently of internal messages.
/// </summary>
/// <typeparam name="THttpRequest">The HTTP request DTO, bound from the request.</typeparam>
/// <typeparam name="TRequest">The command or query dispatched through Synapse.</typeparam>
/// <typeparam name="TResponse">The handler's response.</typeparam>
/// <typeparam name="THttpResponse">The HTTP response DTO written to the wire.</typeparam>
/// <remarks>
///     <para>
///         Prefer <see cref="Endpoint{TRequest,TResponse}" /> unless the wire contract genuinely must
///         differ from the message; this variant costs two mapping methods per endpoint.
///     </para>
///     <para>
///         Derives from <see cref="BoundEndpoint{TBound}" /> rather than
///         <see cref="RawEndpoint{TRequest,TResponse}" /> because the type it binds is
///         <typeparamref name="THttpRequest" />, a wire DTO which is not a message at all and so
///         cannot satisfy that level's <c>IRequest&lt;TResponse&gt;</c> constraint. Its hooks are
///         therefore typed on the wire DTO, not on the message it maps that onto.
///     </para>
/// </remarks>
public abstract class MappedEndpoint<THttpRequest, TRequest, TResponse, THttpResponse>
    : BoundEndpoint<THttpRequest>
    where TRequest : IRequest<TResponse>
    where TResponse : notnull
    where THttpResponse : notnull
{
    private EndpointConfiguration<THttpResponse>? _configuration;
    private IEndpointBinder<THttpRequest>? _binder;

    /// <summary>Configures the endpoint. Called once at startup.</summary>
    /// <param name="builder">The endpoint builder, typed on the HTTP response.</param>
    public virtual void Configure(IEndpointBuilder<THttpResponse> builder)
    {
    }

    /// <summary>Maps the bound HTTP request onto the CQRS message.</summary>
    /// <param name="request">The bound HTTP request DTO.</param>
    /// <returns>The message to dispatch.</returns>
    public abstract TRequest ToRequest(THttpRequest request);

    /// <summary>Maps the handler's response onto the HTTP response DTO.</summary>
    /// <param name="response">The handler's response.</param>
    /// <returns>The DTO to write.</returns>
    public abstract THttpResponse ToResponse(TResponse response);

    /// <summary>Maps a successful HTTP response DTO to an HTTP result.</summary>
    /// <param name="response">The HTTP response DTO.</param>
    /// <param name="context">The HTTP context.</param>
    /// <returns>The HTTP result to write.</returns>
    public virtual IResult OnSuccess(THttpResponse response,
        HttpContext context)
    {
        return TypedResults.Ok(response);
    }

    /// <summary>Not used at this level; configure through the typed overload instead.</summary>
    /// <param name="builder">Unused.</param>
    public sealed override void Configure(IRawEndpointBuilder builder)
    {
    }

    /// <inheritdoc />
    private protected sealed override ValueTask<BindResult<THttpRequest>> BindBoundAsync(HttpContext context)
    {
        return Mapped(_binder).BindAsync(context);
    }

    /// <inheritdoc />
    private protected sealed override ValueTask<IResult> ProduceResultAsync(THttpRequest bound,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var configuration = Mapped(_configuration);
        var invoker = context.RequestServices.GetRequiredService<IHttpInvoker>();

        return invoker.InvokeAsync(
            ToRequest(bound),
            response =>
            {
                var httpResponse = ToResponse(response);
                return configuration.SuccessMapper is not null
                    ? configuration.SuccessMapper(httpResponse)
                    : OnSuccess(httpResponse, context);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>The generated binder over <typeparamref name="THttpRequest" /> knows the answer.</remarks>
    private protected sealed override RequestBodyKind DeclaredRequestBody(string[] httpMethods)
    {
        // Narrowing only, exactly as before: a bodyless verb declares nothing whatever the binder
        // says, including the explicit-[FromBody]-on-a-GET shape SYNE007 warns about. What the binder
        // adds is *which* body — a form-bound message reads one, but not a JSON one.
        return base.DeclaredRequestBody(httpMethods) == RequestBodyKind.None
            ? RequestBodyKind.None
            : _binder?.BodyKind ?? RequestBodyKind.Json;
    }

    /// <inheritdoc />
    private protected sealed override IReadOnlyList<BoundParameterMetadata> DeclaredParameters()
    {
        return _binder?.Parameters ?? [];
    }

    /// <inheritdoc />
    private protected sealed override IReadOnlyList<FormFieldMetadata> DeclaredFormFields()
    {
        return _binder?.FormFields ?? [];
    }

    internal sealed override RawEndpointPlan CreatePlan(EndpointMetadata metadata)
    {
        var builder = new EndpointBuilder<THttpResponse>(metadata);
        Configure(builder);
        var configuration = builder.Build();
        _configuration = configuration;
        ConfiguredProcessors = configuration.Processors;
        _binder = EndpointRegistry.GetBinder<THttpRequest>();

        return new RawEndpointPlan
        {
            Route = configuration.Route,
            HttpMethods = configuration.HttpMethods,
            Processors = configuration.Processors,
            ApplyMetadata = handlerBuilder =>
            {
                // Declared explicitly because a RequestDelegate-shaped endpoint infers nothing.
                RequestBodyMetadata.Apply(handlerBuilder, DeclaredRequestBody(configuration.HttpMethods),
                    typeof(THttpRequest), DeclaredFormFields());

                // Attached as one entry so a single GetMetadata call retrieves the whole list —
                // see BoundParametersMetadata's remarks.
                if (DeclaredParameters() is { Count: > 0 } parameters)
                {
                    handlerBuilder.WithMetadata(new BoundParametersMetadata(parameters));
                }

                // Declared only when the configured mapper writes a body — see docs/known-issues/054.
                handlerBuilder.WithMetadata(new ProducesResponseMetadata(
                    SuccessStatusCode(configuration),
                    configuration.SuccessResponseHasBody ? typeof(THttpResponse) : null));
                handlerBuilder.ProducesValidationProblem();
                configuration.ApplyMetadata(handlerBuilder);
            }
        };
    }

    private static int SuccessStatusCode(EndpointConfiguration<THttpResponse> configuration)
    {
        return configuration.DeclaredSuccessStatusCode ?? StatusCodes.Status200OK;
    }
}
