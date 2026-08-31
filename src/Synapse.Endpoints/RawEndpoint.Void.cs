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
///     The middle level for a command with no response: you write the binding, the base class
///     dispatches <typeparamref name="TRequest" />. Responds with <c>204 No Content</c> unless
///     configured otherwise.
/// </summary>
/// <typeparam name="TRequest">The command to dispatch.</typeparam>
/// <remarks>
///     See <see cref="RawEndpoint{TRequest,TResponse}" />; this is the same level for the arity with no
///     response body.
/// </remarks>
public abstract class RawEndpoint<TRequest> : RawEndpoint
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

        // The onSuccess factory is only invoked by the invoker when the dispatch actually
        // succeeds; a mapped failure is written as-is and never reaches it.
        return await invoker.InvokeAsync(
            bound.Value!,
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

        return new RawEndpointPlan
        {
            Route = configuration.Route,
            HttpMethods = configuration.HttpMethods,
            ApplyMetadata = handlerBuilder =>
            {
                // Declared explicitly because a RequestDelegate-shaped endpoint infers nothing.
                if (DeclaresRequestBody(configuration.HttpMethods))
                {
                    handlerBuilder.Accepts<TRequest>("application/json");
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
