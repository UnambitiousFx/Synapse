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
///     An endpoint that dispatches a command with no response. Responds with
///     <c>204 No Content</c> unless configured otherwise.
/// </summary>
/// <typeparam name="TRequest">The command, which doubles as the HTTP request contract.</typeparam>
/// <remarks>
///     Endpoints are stateless singletons: one instance is created at startup, <c>Configure</c>
///     runs once, and the same instance serves every request. Constructor injection is therefore
///     unavailable by design — take what you need from the <see cref="HttpContext" /> passed to
///     <see cref="OnSuccess" />.
/// </remarks>
public abstract class Endpoint<TRequest> : EndpointBase
    where TRequest : IRequest
{
    /// <summary>Configures the endpoint. Called once at startup.</summary>
    /// <param name="builder">The endpoint builder.</param>
    public virtual void Configure(IEndpointBuilder builder)
    {
    }

    /// <summary>Maps a successful dispatch to an HTTP result.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>The HTTP result to write.</returns>
    public virtual IResult OnSuccess(HttpContext context)
    {
        return TypedResults.NoContent();
    }

    internal override EndpointDescriptor CreateDescriptor(EndpointMetadata metadata)
    {
        var builder = new EndpointBuilder<Unit>(metadata);
        Configure(builder);
        var configuration = builder.Build();
        var binder = EndpointRegistry.GetBinder<TRequest>();

        return new EndpointDescriptor
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

                handlerBuilder.WithMetadata(new ProducesResponseMetadata(SuccessStatusCode(configuration)));
                handlerBuilder.ProducesProblem(StatusCodes.Status400BadRequest);
                configuration.ApplyMetadata(handlerBuilder);
            },
            InvokeAsync = async context =>
            {
                var bound = await binder.BindAsync(context);
                if (!bound.IsSuccess)
                {
                    await TypedResults.Problem(bound.Error, statusCode: StatusCodes.Status400BadRequest)
                        .ExecuteAsync(context);
                    return;
                }

                var invoker = context.RequestServices.GetRequiredService<IHttpInvoker>();

                // The onSuccess factory is only invoked by the invoker when the dispatch actually
                // succeeds; a mapped failure is written as-is and never reaches it.
                var result = await invoker.InvokeAsync(
                    bound.Value!,
                    () => configuration.SuccessMapper is not null
                        ? configuration.SuccessMapper(default)
                        : OnSuccess(context),
                    context.RequestAborted);

                await result.ExecuteAsync(context);
            }
        };
    }

    private static int SuccessStatusCode(EndpointConfiguration<Unit> configuration)
    {
        return configuration.DeclaredSuccessStatusCode ?? StatusCodes.Status204NoContent;
    }
}
