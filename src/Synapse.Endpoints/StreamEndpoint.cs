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
///     An endpoint that streams the items of an <see cref="IStreamRequest{TResponse}" />.
/// </summary>
/// <typeparam name="TRequest">The streaming request, which doubles as the HTTP request contract.</typeparam>
/// <typeparam name="TItem">The streamed item type.</typeparam>
/// <remarks>
///     <para>
///         The response format is negotiated on the <c>Accept</c> header: a value containing
///         <c>text/event-stream</c> yields server-sent events, and anything else (including a missing
///         header) yields a JSON array written incrementally as items arrive. Failed items are skipped,
///         matching <see cref="IHttpInvoker.InvokeStreamAsync{TItem}" />.
///     </para>
///     <para>
///         Derives from <see cref="BoundEndpoint{TBound}" /> rather than
///         <see cref="RawEndpoint{TRequest,TResponse}" /> because it dispatches an
///         <see cref="IStreamRequest{TResponse}" /> and writes the body itself rather than returning a
///         single value to serialize.
///     </para>
/// </remarks>
public abstract class StreamEndpoint<TRequest, TItem> : BoundEndpoint<TRequest>
    where TRequest : IStreamRequest<TItem>
    where TItem : notnull
{
    private readonly JsonTypeInfoCache<TItem> _itemJson = new();
    private IEndpointBinder<TRequest>? _binder;

    /// <summary>Configures the endpoint. Called once at startup.</summary>
    /// <param name="builder">The endpoint builder.</param>
    /// <remarks>
    ///     Takes <see cref="IStreamEndpointBuilder" /> rather than <see cref="IEndpointBuilder" />
    ///     because the latter carries <c>NoContent</c> and <c>StatusCode</c>, which set a success
    ///     mapper this class never consults: a stream's status is committed before the first item is
    ///     produced and its body is the negotiated sequence. Those two calls used to compile here and
    ///     do nothing at all — see docs/known-issues/064.
    /// </remarks>
    public virtual void Configure(IStreamEndpointBuilder builder)
    {
    }

    /// <summary>Not used at this level; configure through the typed overload instead.</summary>
    /// <param name="builder">Unused.</param>
    public sealed override void Configure(IRawEndpointBuilder builder)
    {
    }

    /// <inheritdoc />
    private protected sealed override ValueTask<BindResult<TRequest>> BindBoundAsync(HttpContext context)
    {
        return Mapped(_binder).BindAsync(context);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     The negotiated writer runs inside the returned result rather than here, so the body is
    ///     written at the same point in the pipeline as any other endpoint's result — which is also
    ///     why the exit steps run before the first item is produced. A post-processor here can set
    ///     response headers; it cannot see or change the items.
    /// </remarks>
    private protected sealed override ValueTask<IResult> ProduceResultAsync(TRequest bound,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var invoker = context.RequestServices.GetRequiredService<IHttpInvoker>();
        var items = invoker.InvokeStreamAsync(bound, cancellationToken);
        var typeInfo = _itemJson.Get(context);

        IResult result = WantsServerSentEvents(context)
            ? new ServerSentEventsStreamResult<TItem>(items, typeInfo)
            : new JsonArrayStreamResult<TItem>(items, typeInfo);

        return new ValueTask<IResult>(result);
    }

    /// <inheritdoc />
    /// <remarks>The generated binder knows whether a body is read; the verb alone does not.</remarks>
    private protected sealed override RequestBodyKind DeclaredRequestBody(string[] httpMethods)
    {
        // Narrowing only, exactly as before: a bodyless verb declares nothing whatever the binder
        // says, including the explicit-[FromBody]-on-a-GET shape SYNE007 warns about. What the binder
        // adds is *which* body — a form-bound message reads one, but not a JSON one.
        return base.DeclaredRequestBody(httpMethods) == RequestBodyKind.None
            ? RequestBodyKind.None
            : _binder?.BodyKind ?? RequestBodyKind.Json;
    }

    internal sealed override RawEndpointPlan CreatePlan(EndpointMetadata metadata)
    {
        var builder = new StreamEndpointBuilder(metadata);
        Configure(builder);
        var plan = builder.Build();
        _binder = EndpointRegistry.GetBinder<TRequest>();
        ConfiguredProcessors = plan.Processors;

        return new RawEndpointPlan
        {
            Route = plan.Route,
            HttpMethods = plan.HttpMethods,
            Processors = plan.Processors,
            ApplyMetadata = handlerBuilder =>
            {
                // Declared explicitly because a RequestDelegate-shaped endpoint infers nothing, and
                // guarded on the verb because a bodyless stream (the common GET) accepts nothing. This
                // tier is a body-carrying one whenever its verb is: a POST stream binds TRequest by
                // deserializing the request body exactly as the single-response tiers do, so it owes
                // the same declaration. Omitting it left the input shape absent from the OpenAPI
                // document and left routing unable to reject a wrong content type, which surfaced as a
                // 400 from the binder where every other endpoint answers 415 — see
                // docs/known-issues/065.
                RequestBodyMetadata.Apply(handlerBuilder, DeclaredRequestBody(plan.HttpMethods), typeof(TRequest));

                // The response format is negotiated at request time (see WantsServerSentEvents), so
                // both content types are declared for the same 200 response.
                handlerBuilder.WithMetadata(new ProducesResponseMetadata(
                    StatusCodes.Status200OK,
                    typeof(IAsyncEnumerable<TItem>),
                    ["application/json", "text/event-stream"]));
                handlerBuilder.ProducesValidationProblem();
                plan.ApplyMetadata(handlerBuilder);
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
