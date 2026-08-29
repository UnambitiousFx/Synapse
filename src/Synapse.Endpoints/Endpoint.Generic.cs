using Microsoft.AspNetCore.Http;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Endpoints.Binding;
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
///     <para>
///         The high level, and the one to reach for by default: a route attribute and a class
///         declaration are usually the whole endpoint. The request is bound by a binder the analyzer
///         generates at compile time, with no reflection and nothing to write by hand.
///     </para>
///     <para>
///         This is <see cref="RawEndpoint{TRequest,TResponse}" /> with its binding supplied. Everything
///         else — <c>Configure</c>, <c>OnSuccess</c>, dispatch, failure mapping, the OpenAPI metadata —
///         is inherited unchanged, so the two levels cannot behave differently. If the generated
///         binding is not what you need, derive from that class instead and write
///         <c>BindAsync</c> yourself; nothing else about the endpoint changes.
///     </para>
///     <para>
///         Endpoints are stateless singletons: one instance is created at startup, <c>Configure</c>
///         runs once, and the same instance serves every request. Constructor injection is therefore
///         unavailable by design — take what you need from the <see cref="HttpContext" /> passed to
///         <c>OnSuccess</c>.
///     </para>
/// </remarks>
public abstract class Endpoint<TRequest, TResponse> : RawEndpoint<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : notnull
{
    private IEndpointBinder<TRequest>? _binder;

    /// <inheritdoc />
    /// <remarks>
    ///     Sealed: the generated binder is what makes this the high level. Override the binding by
    ///     deriving from <see cref="RawEndpoint{TRequest,TResponse}" /> instead.
    /// </remarks>
    public sealed override ValueTask<BindResult<TRequest>> BindAsync(HttpContext context)
    {
        return Mapped(_binder).BindAsync(context);
    }

    internal sealed override RawEndpointPlan CreatePlan(EndpointMetadata metadata)
    {
        // Resolved after the plan, matching the order the route and binder were resolved in before:
        // an endpoint missing both reports its missing route first, which is the more useful error.
        var plan = base.CreatePlan(metadata);
        _binder = EndpointRegistry.GetBinder<TRequest>();
        return plan;
    }
}
