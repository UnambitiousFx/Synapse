using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Functional;
using UnambitiousFx.Functional.AspNetCore.Http;
using UnambitiousFx.Functional.AspNetCore.Mappers;
using UnambitiousFx.Synapse.Endpoints.Binding;
using UnambitiousFx.Synapse.Endpoints.Builders;
using UnambitiousFx.Synapse.Endpoints.Internal;

// UnambitiousFx.Functional declares an IResult of its own, so the unqualified name has to be
// pinned to ASP.NET's — the same device Synapse.AspNetCore's IHttpInvoker uses.
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace UnambitiousFx.Synapse.Endpoints;

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
