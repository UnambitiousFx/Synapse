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

    internal sealed override RawEndpointPlan CreatePlan(EndpointMetadata metadata)
    {
        var plan = base.CreatePlan(metadata);
        _binder = EndpointRegistry.GetBinder<TRequest>();
        return plan;
    }
}
