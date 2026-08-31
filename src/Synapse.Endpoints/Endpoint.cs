using Microsoft.AspNetCore.Http;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Endpoints.Binding;
using UnambitiousFx.Synapse.Endpoints.Internal;

namespace UnambitiousFx.Synapse.Endpoints;

/// <summary>
///     An endpoint that dispatches a command with no response. Responds with
///     <c>204 No Content</c> unless configured otherwise.
/// </summary>
/// <typeparam name="TRequest">The command, which doubles as the HTTP request contract.</typeparam>
/// <remarks>
///     See <see cref="Endpoint{TRequest,TResponse}" />; this is the same level for the arity with no
///     response body. It is <see cref="RawEndpoint{TRequest}" /> with its binding supplied by the
///     generated binder.
/// </remarks>
public abstract class Endpoint<TRequest> : RawEndpoint<TRequest>
    where TRequest : IRequest
{
    private IEndpointBinder<TRequest>? _binder;

    /// <inheritdoc />
    /// <remarks>
    ///     Sealed: the generated binder is what makes this the high level. Override the binding by
    ///     deriving from <see cref="RawEndpoint{TRequest}" /> instead.
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
        var plan = base.CreatePlan(metadata);
        _binder = EndpointRegistry.GetBinder<TRequest>();
        return plan;
    }
}
