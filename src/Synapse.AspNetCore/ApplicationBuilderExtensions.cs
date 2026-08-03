using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.AspNetCore.Http;

namespace UnambitiousFx.Synapse.AspNetCore;

/// <summary>
///     Extension methods for configuring Synapse middleware in the ASP.NET Core request pipeline.
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    ///     Adds middleware that recovers the flow identity of an incoming request from W3C trace context and
    ///     writes the resulting trace id back onto the response.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Identity comes from the inbound <c>traceparent</c> header: its trace id becomes
    ///         <see cref="IContext.TraceId" /> and its span id becomes <see cref="IContext.CausationId" />, so a
    ///         request arriving from another service continues the same flow and the same trace. The
    ///         <c>baggage</c> header is recovered alongside it for business values. Synapse reads no identity
    ///         header of its own — a request with no <c>traceparent</c> simply starts a new flow.
    ///     </para>
    ///     <para>
    ///         What is recovered is stored in <see cref="IInboundContextStore" /> rather than applied to an
    ///         existing context, so it is honoured no matter when the context is first resolved.
    ///     </para>
    ///     <para>
    ///         Two response headers are written, and only when a context was actually created during the request —
    ///         so routes that never touch the mediator, such as static files and health checks, do not get a trace
    ///         id invented for them. <see cref="PropagationOptions.TraceIdHeaderName" /> carries the bare 32-character
    ///         hex trace id, which a person can paste straight into a tracing backend; the W3C Trace Context Level 2
    ///         <c>traceresponse</c> header carries the full form for conformant tooling.
    ///     </para>
    ///     <para>
    ///         <b>Security:</b> on an internet-facing application set
    ///         <see cref="PropagationOptions.TrustIncomingHeader" /> to <c>false</c>. See that property for what
    ///         changes.
    ///     </para>
    ///     <para>
    ///         For a browser to send <c>traceparent</c> and read the trace id back, the app's CORS policy needs
    ///         those headers in <c>Access-Control-Allow-Headers</c> and
    ///         <c>Access-Control-Expose-Headers</c> respectively.
    ///     </para>
    /// </remarks>
    /// <param name="app">The application builder.</param>
    /// <param name="configure">An optional delegate to configure the middleware.</param>
    /// <returns>The application builder for chaining.</returns>
    public static IApplicationBuilder UseSynapsePropagation(this IApplicationBuilder app,
        Action<PropagationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(app);

        var options = new PropagationOptions();
        configure?.Invoke(options);

        return app.Use(async (ctx, next) =>
        {
            CaptureInboundState(ctx, options);
            ctx.Response.OnStarting(() => OnStartingResponse(ctx, options));
            await next();
        });
    }

    private static void CaptureInboundState(HttpContext httpContext,
        PropagationOptions options)
    {
        var store = httpContext.RequestServices.GetService<IInboundContextStore>();
        if (store is null)
        {
            return;
        }

        var carrier = new HttpRequestPropagationCarrier(httpContext.Request);
        var propagator = httpContext.RequestServices.GetService<IContextPropagator>();
        var inbound = propagator?.Extract(carrier) ?? PropagatedContext.None;

        store.Inbound = options.TrustIncomingHeader
            ? inbound
            : Untrusted(inbound, options);
    }

    /// <summary>
    ///     Reduces inbound state to what is safe to accept from an arbitrary caller: nothing that becomes this
    ///     flow's identity, with the caller's trace id demoted to a baggage entry so it stays queryable.
    /// </summary>
    private static PropagatedContext Untrusted(PropagatedContext inbound,
        PropagationOptions options)
    {
        Dictionary<string, string>? baggage = null;

        if (inbound.Trace != default)
        {
            baggage = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [options.ClientTraceIdBaggageKey] = inbound.Trace.TraceId.ToHexString()
            };
        }

        // Both the trace context and the inbound baggage are dropped: adopting the trace context would make the
        // caller's trace id this flow's identity, and inbound baggage is caller-controlled text that would
        // otherwise land in this service's log scope and be forwarded onwards.
        //
        // Clearing the trace context is not on its own enough to refuse it. ASP.NET Core's own request
        // instrumentation parses traceparent before any middleware runs, so Activity.Current is already parented
        // to the caller and the context factory would read the caller's trace id from there instead — the header
        // would be honoured after all (see known issue 032). SuppressAmbientTrace is what closes that path.
        return new PropagatedContext(default, baggage, SuppressAmbientTrace: true);
    }

    private static Task OnStartingResponse(HttpContext httpContext,
        PropagationOptions options)
    {
        var accessor = httpContext.RequestServices.GetService<IContextAccessor>();
        if (accessor is not { IsInitialized: true })
        {
            return Task.CompletedTask;
        }

        var context = accessor.Context;
        httpContext.Response.Headers.TryAdd(options.TraceIdHeaderName, context.TraceId);

        if (options.EmitTraceResponse &&
            TryBuildTraceResponse(context, out var traceResponse))
        {
            httpContext.Response.Headers.TryAdd(PropagationKeys.TraceResponse, traceResponse);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    ///     Formats the W3C Trace Context Level 2 <c>traceresponse</c> value for this response.
    /// </summary>
    /// <remarks>
    ///     Built from <see cref="IContext.TraceId" /> plus the current span rather than from
    ///     <c>Activity.Id</c>, which would already be a formatted traceparent. The difference matters in
    ///     untrusted mode: the host's own instrumentation may have adopted the caller's inbound trace context,
    ///     so <c>Activity.Id</c> would report the caller's trace id while Synapse reports the server-minted one.
    ///     Deriving from the context keeps the two response headers in agreement.
    ///     <para>
    ///         Returns <c>false</c> when there is no W3C activity to describe — <c>traceresponse</c> reports the
    ///         response's span, and inventing one would be a lie.
    ///     </para>
    /// </remarks>
    private static bool TryBuildTraceResponse(IContext context,
        out string value)
    {
        var activity = Activity.Current;
        if (activity is null ||
            activity.IdFormat != ActivityIdFormat.W3C)
        {
            value = string.Empty;
            return false;
        }

        var flags = (activity.ActivityTraceFlags & ActivityTraceFlags.Recorded) != 0
            ? "01"
            : "00";

        value = $"00-{context.TraceId}-{activity.SpanId.ToHexString()}-{flags}";
        return true;
    }
}
