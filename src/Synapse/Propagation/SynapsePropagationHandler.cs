using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Contexts;

namespace UnambitiousFx.Synapse.Propagation;

/// <summary>
///     An <see cref="HttpMessageHandler" /> that stamps the current Synapse flow identity onto every outgoing
///     request.
/// </summary>
/// <remarks>
///     <para>
///         Add it to a named client so calls made while handling a message stay part of the same flow:
///     </para>
///     <code>
///     services.AddHttpClient("billing")
///             .AddHttpMessageHandler&lt;SynapsePropagationHandler&gt;();
///     </code>
///     <para>
///         <c>SocketsHttpHandler</c> already injects <c>traceparent</c> and <c>tracestate</c> on its own, so this
///         handler exists for the part the platform cannot know about: the baggage that Synapse keeps on the
///         context rather than on the ambient <c>Activity</c>.
///     </para>
///     <para>
///         The context is read from the execution context, not from an injected
///         <see cref="IContextAccessor" />. <c>IHttpClientFactory</c> constructs message handlers in a scope of
///         its own and caches them across requests, so an injected scoped accessor would never be the accessor of
///         the unit of work making the call: it would report no context and this handler would silently stamp
///         nothing (see known issue 033).
///     </para>
/// </remarks>
public sealed class SynapsePropagationHandler : DelegatingHandler
{
    private readonly IContextPropagator _propagator;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SynapsePropagationHandler" /> class.
    /// </summary>
    /// <param name="propagator">The propagator that writes the state onto the request.</param>
    public SynapsePropagationHandler(IContextPropagator propagator)
    {
        ArgumentNullException.ThrowIfNull(propagator);
        _propagator = propagator;
    }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Only propagate when a context already exists. Creating one here would invent a flow for an outbound
        // call that no unit of work asked for.
        if (AmbientContext.Value is { } context)
        {
            _propagator.Inject(context, new HttpRequestMessagePropagationCarrier(request));
        }

        return base.SendAsync(request, cancellationToken);
    }
}
