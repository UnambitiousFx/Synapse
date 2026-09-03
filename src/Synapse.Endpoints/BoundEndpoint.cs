using Microsoft.AspNetCore.Http;
using UnambitiousFx.Synapse.Endpoints.Binding;
using UnambitiousFx.Synapse.Endpoints.Builders;
using UnambitiousFx.Synapse.Endpoints.Internal;

namespace UnambitiousFx.Synapse.Endpoints;

/// <summary>
///     The lifecycle seam shared by every tier that binds something: the three hooks, and the
///     "on the way out" step that runs whatever produced the result.
/// </summary>
/// <typeparam name="TBound">
///     What the tier binds. For <see cref="MappedEndpoint{THttpRequest,TRequest,TResponse,THttpResponse}" />
///     that is the wire DTO, not the message — the hooks run around binding, and binding is what
///     produces a DTO.
/// </typeparam>
/// <remarks>
///     <para>
///         Not a tier, and not derivable outside this library: its constructor is
///         <c>private protected</c>, the same device <see cref="EndpointBase" /> uses to force
///         endpoints through one of the library's own base classes. Derive from
///         <see cref="Endpoint{TRequest,TResponse}" /> or one of its siblings instead.
///     </para>
///     <para>
///         It exists so the ordering contract is written once. The four sealed tiers each have their
///         own <c>HandleAsync</c>, so declaring the hooks per tier would put four copies of that
///         contract in the codebase — the drift <see cref="EndpointBuilderCore" /> and
///         <see cref="RawEndpointPlan" /> already exist to prevent.
///     </para>
///     <para>
///         The documented order is: pre-processors, <c>BindAsync</c>,
///         <see cref="OnBindFailedAsync" /> when it failed, <see cref="OnBeforeHandleAsync" /> when it
///         did not, dispatch and mapping, <see cref="OnAfterHandleAsync" />, post-processors, then the
///         result is written. Steps from <see cref="OnAfterHandleAsync" /> onwards run on every path.
///     </para>
/// </remarks>
public abstract class BoundEndpoint<TBound> : RawEndpoint
{
    private protected BoundEndpoint()
    {
    }

    /// <summary>The processors resolved at startup, or null before the endpoint is mapped.</summary>
    private protected EndpointProcessors? ConfiguredProcessors { get; set; }

    /// <summary>The registered processors, failing with an explanation when unmapped.</summary>
    /// <remarks>
    ///     Read at the very top of every tier's <c>HandleAsync</c>, which is now before the first
    ///     <c>Mapped(_configuration)</c> call. Going through <c>Mapped</c> is what keeps an unmapped
    ///     endpoint answering with the message that names <c>EndpointHarness</c> rather than a bare
    ///     <see cref="NullReferenceException" /> — see docs/known-issues/056.
    /// </remarks>
    private protected EndpointProcessors ResolvedProcessors => Mapped(ConfiguredProcessors);

    /// <summary>
    ///     Runs after binding succeeds and before the message is dispatched.
    /// </summary>
    /// <param name="request">The bound request.</param>
    /// <param name="context">The HTTP context.</param>
    /// <param name="cancellationToken">Cancellation token, tied to the request.</param>
    /// <returns>
    ///     A result to write instead of dispatching, or <see langword="null" /> to carry on.
    ///     Short-circuiting here still runs <see cref="OnAfterHandleAsync" /> and any post-processors.
    /// </returns>
    /// <remarks>
    ///     This is the typed seam: it is the only hook that sees the bound request. Cross-cutting code
    ///     that needs only the context belongs in an <see cref="IEndpointPreProcessor" />, which also
    ///     runs earlier — before binding.
    /// </remarks>
    protected virtual ValueTask<IResult?> OnBeforeHandleAsync(TBound request,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        return default;
    }

    /// <summary>
    ///     Runs on the way out, before the result is written.
    /// </summary>
    /// <param name="result">The result the endpoint produced.</param>
    /// <param name="context">The HTTP context.</param>
    /// <param name="cancellationToken">Cancellation token, tied to the request.</param>
    /// <returns>The result to write. Return <paramref name="result" /> to leave it unchanged.</returns>
    /// <remarks>
    ///     Runs whatever produced the result: a pre-processor's short circuit, a binding failure's
    ///     <c>400</c>, a mapped dispatch failure, or the success mapper. Nothing has executed the
    ///     result yet, so writing to <c>context.Response.Headers</c> here still reaches the wire.
    /// </remarks>
    protected virtual ValueTask<IResult> OnAfterHandleAsync(IResult result,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        return new ValueTask<IResult>(result);
    }

    /// <summary>
    ///     Runs when binding fails, before the <c>400</c> is written.
    /// </summary>
    /// <param name="bound">The failed bind result, carrying every collected error.</param>
    /// <param name="context">The HTTP context.</param>
    /// <param name="cancellationToken">Cancellation token, tied to the request.</param>
    /// <returns>The result to write. The default is the <c>400</c> the binding produced.</returns>
    protected virtual ValueTask<IResult> OnBindFailedAsync(BindResult<TBound> bound,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        return new ValueTask<IResult>(bound.Problem());
    }

    /// <summary>
    ///     Runs the two exit steps every path shares: the after-hook, then the post-processors.
    /// </summary>
    /// <param name="result">The result the endpoint produced.</param>
    /// <param name="processors">The resolved processors, read once by the caller.</param>
    /// <param name="context">The HTTP context.</param>
    /// <param name="cancellationToken">Cancellation token, tied to the request.</param>
    /// <returns>The result to write.</returns>
    private protected async ValueTask<IResult> FinishAsync(IResult result,
        EndpointProcessors processors,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var mapped = await OnAfterHandleAsync(result, context, cancellationToken);
        return await processors.RunPostAsync(mapped, context, cancellationToken);
    }
}
