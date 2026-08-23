using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.AspNetCore.Http;
using UnambitiousFx.Synapse.Endpoints.Binding;
using UnambitiousFx.Synapse.Endpoints.Builders;
using UnambitiousFx.Synapse.Endpoints.Internal;

namespace UnambitiousFx.Synapse.Endpoints;

/// <summary>
///     An endpoint that dispatches <typeparamref name="TRequest" /> and returns
///     <typeparamref name="TResponse" />. Responds with <c>200 OK</c> and the response as the body
///     unless configured otherwise.
/// </summary>
/// <typeparam name="TRequest">The command or query, which doubles as the HTTP request contract.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <remarks>
///     Endpoints are stateless singletons: one instance is created at startup, <c>Configure</c>
///     runs once, and the same instance serves every request. Constructor injection is therefore
///     unavailable by design — take what you need from the <see cref="HttpContext" /> passed to
///     <see cref="OnSuccess" />.
/// </remarks>
public abstract class Endpoint<TRequest, TResponse> : EndpointBase
    where TRequest : IRequest<TResponse>
    where TResponse : notnull
{
    /// <summary>Configures the endpoint. Called once at startup.</summary>
    /// <param name="builder">The endpoint builder.</param>
    public virtual void Configure(IEndpointBuilder<TResponse> builder)
    {
    }

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

    internal override EndpointDescriptor CreateDescriptor(EndpointMetadata metadata)
    {
        var builder = new EndpointBuilder<TResponse>(metadata);
        Configure(builder);
        var configuration = builder.Build();
        var binder = EndpointRegistry.GetBinder<TRequest>();

        return new EndpointDescriptor
        {
            Route = configuration.Route,
            HttpMethods = configuration.HttpMethods,
            ApplyMetadata = configuration.ApplyMetadata,
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

                // Failures flow through the registered IFailureHttpMapper, unchanged.
                var result = await invoker.InvokeAsync(
                    bound.Value!,
                    response => configuration.SuccessMapper is not null
                        ? configuration.SuccessMapper(response)
                        : OnSuccess(response, context),
                    context.RequestAborted);

                await result.ExecuteAsync(context);
            }
        };
    }
}
