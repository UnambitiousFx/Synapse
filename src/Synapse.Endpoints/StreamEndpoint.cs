using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.AspNetCore.Http;
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
///         Derives from <see cref="EndpointLifecycle{TRequest}" /> rather than
///         <see cref="BoundEndpoint{TRequest,TResponse}" /> because it dispatches an
///         <see cref="IStreamRequest{TResponse}" /> and writes the body itself rather than returning a
///         single value to serialize.
///     </para>
/// </remarks>
public abstract class StreamEndpoint<TRequest, TItem> : EndpointLifecycle<TRequest>
    where TRequest : IStreamRequest<TItem>
    where TItem : notnull
{
    private readonly JsonTypeInfoCache<TItem> _itemJson = new();

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

    internal sealed override RawEndpointPlan CreatePlan(EndpointMetadata metadata)
    {
        var builder = new StreamEndpointBuilder(metadata);
        Configure(builder);
        var plan = builder.Build();

        // The response format is negotiated at request time (see WantsServerSentEvents), so both
        // content types are declared for the same 200 response. The request body this tier reads is
        // declared by BuildPlan on the same terms as every other tier's: a POST stream deserializes
        // TRequest from the body exactly as the single-response tiers do — see
        // docs/known-issues/065.
        return BuildPlan(
            plan.Route,
            plan.HttpMethods,
            plan.Processors,
            new ProducesResponseMetadata(
                StatusCodes.Status200OK,
                typeof(IAsyncEnumerable<TItem>),
                ["application/json", "text/event-stream"]),
            plan.ApplyMetadata);
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
