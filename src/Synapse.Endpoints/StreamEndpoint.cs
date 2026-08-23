using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.AspNetCore.Http;
using UnambitiousFx.Synapse.Endpoints.Binding;
using UnambitiousFx.Synapse.Endpoints.Builders;
using UnambitiousFx.Synapse.Endpoints.Internal;

namespace UnambitiousFx.Synapse.Endpoints;

/// <summary>
///     An endpoint that streams the items of an <see cref="IStreamRequest{TResponse}" />.
/// </summary>
/// <typeparam name="TRequest">The streaming request, which doubles as the HTTP request contract.</typeparam>
/// <typeparam name="TItem">The streamed item type.</typeparam>
/// <remarks>
///     The response format is negotiated on the <c>Accept</c> header: a value containing
///     <c>text/event-stream</c> yields server-sent events, and anything else (including a missing
///     header) yields a JSON array written incrementally as items arrive. Failed items are skipped,
///     matching <see cref="IHttpInvoker.InvokeStreamAsync{TItem}" />.
/// </remarks>
public abstract class StreamEndpoint<TRequest, TItem> : EndpointBase
    where TRequest : IStreamRequest<TItem>
    where TItem : notnull
{
    private readonly JsonTypeInfoCache<TItem> _itemJson = new();

    /// <summary>Configures the endpoint. Called once at startup.</summary>
    /// <param name="builder">The endpoint builder.</param>
    public virtual void Configure(IEndpointBuilder builder)
    {
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
                var items = invoker.InvokeStreamAsync(bound.Value!, context.RequestAborted);
                var typeInfo = _itemJson.Get(context);

                if (WantsServerSentEvents(context))
                {
                    await StreamResponseWriter.WriteServerSentEventsAsync(context, items, typeInfo);
                    return;
                }

                await StreamResponseWriter.WriteJsonArrayAsync(context, items, typeInfo);
            }
        };
    }

    private static bool WantsServerSentEvents(HttpContext context)
    {
        foreach (var value in context.Request.Headers.Accept)
        {
            if (value is not null &&
                value.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
