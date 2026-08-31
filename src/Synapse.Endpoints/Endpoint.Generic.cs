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

    /// <inheritdoc />
    /// <remarks>
    ///     The generated binder reports whether it deserializes the message, which is what decides
    ///     this — a verb that carries a body but whose every property binds from the route, query or a
    ///     header reads nothing. See <c>docs/known-issues/067</c>.
    /// </remarks>
    private protected sealed override bool DeclaresRequestBody(string[] httpMethods)
    {
        // Both must hold: the verb has to be one that carries a body at all, and the binder has
        // to actually read one. Narrowing only — a bodyless verb declared nothing before and
        // still does, including the explicit-[FromBody]-on-a-GET shape SYNE007 warns about.
        return base.DeclaresRequestBody(httpMethods) && (_binder?.ReadsRequestBody ?? true);
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
