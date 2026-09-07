using Microsoft.AspNetCore.Http;
using UnambitiousFx.Synapse.Abstractions;

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
///         declaration are usually the whole endpoint. The request is bound by code the analyzer
///         generates at compile time, with no reflection and nothing to write by hand.
///     </para>
///     <para>
///         An empty marker over <see cref="RawEndpoint{TRequest,TResponse}" />: deriving from this
///         class is what tells the analyzer to write <c>BindAsync</c>, and the analyzer writes it as a
///         member of the endpoint's own <c>partial</c> class. That makes the analyzer a
///         <em>requirement</em> rather than a convenience — an endpoint at this level that the
///         analyzer did not see does not compile, because <c>BindAsync</c> is still abstract.
///     </para>
///     <para>
///         Everything else — <c>Configure</c>, <c>OnSuccess</c>, dispatch, failure mapping, the
///         OpenAPI metadata — is inherited from that class unchanged, so the two levels cannot behave
///         differently. If the generated binding is not what you need, derive from
///         <see cref="RawEndpoint{TRequest,TResponse}" /> instead and write <c>BindAsync</c>
///         yourself; nothing else about the endpoint changes.
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
    where TResponse : notnull;
