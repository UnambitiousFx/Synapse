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
///     Prefer <see cref="Endpoint{TRequest,TResponse}" /> unless the wire contract genuinely must
///     differ from the message; this variant costs two mapping methods per endpoint.
/// </remarks>
public abstract class MappedEndpoint<THttpRequest, TRequest, TResponse, THttpResponse> : EndpointBase
    where TRequest : IRequest<TResponse>
    where TResponse : notnull
    where THttpResponse : notnull
{
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

    internal override EndpointDescriptor CreateDescriptor(EndpointMetadata metadata)
    {
        var builder = new EndpointBuilder<THttpResponse>(metadata);
        Configure(builder);
        var configuration = builder.Build();
        var binder = EndpointRegistry.GetBinder<THttpRequest>();

        return new EndpointDescriptor
        {
            Route = configuration.Route,
            HttpMethods = configuration.HttpMethods,
            ApplyMetadata = handlerBuilder =>
            {
                // Declared explicitly because a RequestDelegate-shaped endpoint infers nothing.
                if (!HttpMethodHelpers.IsBodylessVerb(configuration.HttpMethods))
                {
                    handlerBuilder.Accepts(typeof(THttpRequest), "application/json");
                }

                handlerBuilder.WithMetadata(
                    new ProducesResponseMetadata(SuccessStatusCode(configuration), typeof(THttpResponse)));
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
                var result = await invoker.InvokeAsync(
                    ToRequest(bound.Value!),
                    response =>
                    {
                        var httpResponse = ToResponse(response);
                        return configuration.SuccessMapper is not null
                            ? configuration.SuccessMapper(httpResponse)
                            : OnSuccess(httpResponse, context);
                    },
                    context.RequestAborted);

                await result.ExecuteAsync(context);
            }
        };
    }

    private static int SuccessStatusCode(EndpointConfiguration<THttpResponse> configuration)
    {
        return configuration.DeclaredSuccessStatusCode ?? StatusCodes.Status200OK;
    }
}
