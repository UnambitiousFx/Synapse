using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using UnambitiousFx.Synapse.Endpoints.Binding;
using UnambitiousFx.Synapse.Endpoints.Builders;
using UnambitiousFx.Synapse.Endpoints.Internal;

namespace UnambitiousFx.Synapse.Endpoints;

/// <summary>
///     The lifecycle seam shared by every tier that binds something: the three hooks, and the
///     "on the way out" step that runs whatever produced the result.
/// </summary>
/// <typeparam name="TRequest">
///     What the tier binds. For <see cref="ContractEndpoint{THttpRequest,TRequest,TResponse,THttpResponse}" />
///     that is the wire DTO, not the message — the hooks run around binding, and binding is what
///     produces a DTO.
/// </typeparam>
/// <remarks>
///     <para>
///         Not a tier, and not derivable outside this library: its constructor is
///         <c>private protected</c>, the same device <see cref="SynapseEndpoint" /> uses to force
///         endpoints through one of the library's own base classes. Derive from
///         <see cref="Endpoint{TRequest,TResponse}" /> or one of its siblings instead.
///     </para>
///     <para>
///         Declaring the three hooks per tier would put four copies of those declarations in the
///         codebase, so they live here instead, alongside the whole documented order, in this type's
///         <c>HandleAsync</c>. A tier contributes only <see cref="ProduceResultAsync" /> and the
///         response half of <see cref="BuildPlan" />; factoring shared concerns onto one type this way is the
///         same device <see cref="EndpointBuilderCore" /> and <see cref="RawEndpointPlan" /> use
///         elsewhere in the library.
///     </para>
///     <para>
///         The documented order is: pre-processors, <c>BindAsync</c>,
///         <see cref="OnBindFailedAsync" /> when it failed, <see cref="OnBeforeHandleAsync" /> when it
///         did not, dispatch and mapping, <see cref="OnAfterHandleAsync" />, post-processors, then the
///         result is written. Steps from <see cref="OnAfterHandleAsync" /> onwards run on every path
///         <em>that produces a result</em> — an unhandled exception from dispatch, a mapper, a hook, or
///         a processor bypasses them entirely and is the ASP.NET exception handler's business, not
///         this type's.
///     </para>
/// </remarks>
public abstract class EndpointLifecycle<TRequest> : RawEndpoint
{
    /// <summary>The processors resolved at startup, or null before the endpoint is mapped.</summary>
    /// <remarks>
    ///     Written only by <see cref="BuildPlan" />, which every binding tier's <c>CreatePlan</c> goes
    ///     through, so a tier can no longer register processors on its plan and forget to hand them to
    ///     the endpoint that has to run them.
    /// </remarks>
    private EndpointProcessors? _configuredProcessors;

    private protected EndpointLifecycle()
    {
    }

    /// <summary>The registered processors, failing with an explanation when unmapped.</summary>
    /// <remarks>
    ///     Read at the very top of <c>HandleAsync</c>, before the first <c>Mapped(_configuration)</c>
    ///     call a tier's <see cref="ProduceResultAsync" /> makes. Going through <c>Mapped</c> is what
    ///     keeps an unmapped endpoint answering with the message that names <c>EndpointHarness</c>
    ///     rather than a bare <see cref="NullReferenceException" /> — see docs/known-issues/056.
    /// </remarks>
    private protected EndpointProcessors ResolvedProcessors => Mapped(_configuredProcessors);

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
    protected virtual ValueTask<IResult?> OnBeforeHandleAsync(TRequest request,
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
    ///     <para>
    ///         Runs whatever produced a result: a pre-processor's short circuit, a binding failure's
    ///         <c>400</c>, a mapped dispatch failure, or the success mapper. It does not run when
    ///         dispatch, a mapper, or an earlier hook throws — that exception bypasses this hook, the
    ///         post-processors, and the write, and reaches the ASP.NET exception handler instead.
    ///     </para>
    ///     <para>
    ///         Nothing has executed <paramref name="result" /> yet, so writing to
    ///         <c>context.Response.Headers</c> here still reaches the wire — but
    ///         <c>context.Response.StatusCode</c> is still whatever it defaulted to, because the result
    ///         has not run yet either. Pattern-match <paramref name="result" /> against
    ///         <see cref="IStatusCodeHttpResult" /> to read the status it is about to write; that fails
    ///         for a result that does not implement it, including the stream tier's negotiated writer.
    ///     </para>
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
    protected virtual ValueTask<IResult> OnBindFailedAsync(BindResult<TRequest> bound,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        return new ValueTask<IResult>(bound.Problem());
    }

    /// <summary>
    ///     Runs <see cref="OnBindFailedAsync" /> and guards its result, naming that hook rather than
    ///     <c>HandleAsync</c> if it returns null.
    /// </summary>
    /// <param name="bound">The failed bind result, carrying every collected error.</param>
    /// <param name="context">The HTTP context.</param>
    /// <param name="cancellationToken">Cancellation token, tied to the request.</param>
    /// <returns>The result to write.</returns>
    private protected async ValueTask<IResult> BindFailedResultAsync(BindResult<TRequest> bound,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        return await OnBindFailedAsync(bound, context, cancellationToken)
               ?? throw new InvalidOperationException(
                   $"Endpoint '{GetType()}' returned a null result from OnBindFailedAsync. Return " +
                   "the failed bind's 400 to leave the response unchanged, or a replacement to " +
                   "change it.");
    }

    /// <summary>Binds the request onto whatever this tier binds.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>The bound value, or the failures preventing it.</returns>
    /// <remarks>
    ///     <para>
    ///         Declared once here rather than per tier, because <typeparamref name="TRequest" />
    ///         <em>is</em> the bound type on every tier: the message for the dispatching and streaming
    ///         tiers, the request contract for the self-handled ones, and the wire DTO for
    ///         <see cref="ContractEndpoint{THttpRequest,TRequest,TResponse,THttpResponse}" />, which
    ///         passes that DTO as this class's type argument. Every tier used to declare it
    ///         identically and forward to a <c>private protected</c> twin declared here.
    ///     </para>
    ///     <para>
    ///         What differs per tier is who implements it. The two <c>BoundEndpoint&lt;…&gt;</c> tiers
    ///         leave it to the author; every generated tier has it emitted into the endpoint's own
    ///         <c>partial</c>, which is why an endpoint the analyzer never saw does not compile — the
    ///         binding is an <c>override</c> the compiler requires, not a binder looked up at startup.
    ///     </para>
    ///     <para>
    ///         A failure short-circuits through <see cref="OnBindFailedAsync" /> to a <c>400</c>
    ///         carrying every collected error, and nothing is dispatched.
    ///     </para>
    /// </remarks>
    public abstract ValueTask<BindResult<TRequest>> BindAsync(HttpContext context);

    /// <summary>Not used by any binding tier; configure through the typed overload the tier declares.</summary>
    /// <param name="builder">Unused.</param>
    /// <remarks>
    ///     Sealed once here rather than once per tier. Every tier below configures through
    ///     its own typed <c>Configure</c> overload, so leaving the low-level one open would let a
    ///     subclass override a hook that is never called and wonder why its configuration is ignored.
    ///     Sealing turns that into a compile error.
    /// </remarks>
    public sealed override void Configure(IRawEndpointBuilder builder)
    {
    }

    /// <summary>Dispatches the bound value and maps the outcome to a result.</summary>
    /// <param name="bound">The bound value.</param>
    /// <param name="context">The HTTP context.</param>
    /// <param name="cancellationToken">Cancellation token, tied to the request.</param>
    /// <returns>The result the endpoint produced.</returns>
    /// <remarks>
    ///     Everything a tier does that is its own: which invoker overload it calls, how it maps a
    ///     success, and — for the streaming tier — building the negotiated writer rather than
    ///     dispatching for a single value at all, which is why this is not called <c>DispatchAsync</c>.
    /// </remarks>
    private protected abstract ValueTask<IResult> ProduceResultAsync(TRequest bound,
        HttpContext context,
        CancellationToken cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    ///     Sealed, and sealed here rather than on each tier so the documented order exists in one
    ///     place. Change the binding through <c>BindAsync</c>, the response through <c>OnSuccess</c>
    ///     or the builder, and wrap the exchange through <see cref="OnBeforeHandleAsync" />,
    ///     <see cref="OnAfterHandleAsync" />, <see cref="OnBindFailedAsync" /> or a registered
    ///     <see cref="IEndpointPreProcessor" /> / <see cref="IEndpointPostProcessor" />.
    /// </remarks>
    public sealed override async ValueTask<IResult> HandleAsync(HttpContext context,
        CancellationToken cancellationToken)
    {
        // Read before anything else so an unmapped endpoint reports that, rather than failing later
        // and less clearly.
        var processors = ResolvedProcessors;

        var result = await RunLifecycleAsync(processors, context, cancellationToken);

        // Unconditional, and that is the point: every path that produced a result above arrives
        // here, so the exit steps cannot be skipped by a tier forgetting to call them.
        var mapped = await OnAfterHandleAsync(result, context, cancellationToken)
                     ?? throw new InvalidOperationException(
                         $"Endpoint '{GetType()}' returned a null result from OnAfterHandleAsync. " +
                         "Return the result it was given to leave the response unchanged, or a " +
                         "replacement to change it.");

        return await processors.RunPostAsync(mapped, context, cancellationToken);
    }

    /// <summary>Runs steps 1 to 5 and returns whichever of them produced the result.</summary>
    /// <param name="processors">The resolved processors, read once by the caller.</param>
    /// <param name="context">The HTTP context.</param>
    /// <param name="cancellationToken">Cancellation token, tied to the request.</param>
    /// <returns>The result to hand to the exit steps.</returns>
    private async ValueTask<IResult> RunLifecycleAsync(EndpointProcessors processors,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var shortCircuit = await processors.RunPreAsync(context, cancellationToken);
        if (shortCircuit is not null)
        {
            return shortCircuit;
        }

        var bound = await BindAsync(context);
        if (!bound.IsSuccess)
        {
            return await BindFailedResultAsync(bound, context, cancellationToken);
        }

        var before = await OnBeforeHandleAsync(bound.Value!, context, cancellationToken);
        if (before is not null)
        {
            return before;
        }

        return await ProduceResultAsync(bound.Value!, context, cancellationToken);
    }

    /// <summary>
    ///     Builds the plan every binding tier shares: where it sits on the route table, the
    ///     processors it registered, and the OpenAPI metadata that follows from binding
    ///     <typeparamref name="TRequest" />.
    /// </summary>
    /// <param name="route">The resolved route template.</param>
    /// <param name="httpMethods">The resolved HTTP methods.</param>
    /// <param name="processors">The pre- and post-processors the endpoint registered.</param>
    /// <param name="response">The success response this tier declares.</param>
    /// <param name="applyConfigured">The metadata the endpoint's own builder accumulated.</param>
    /// <returns>The plan.</returns>
    /// <remarks>
    ///     <para>
    ///         The tiers' plans differ in two things — which builder they configure through, and what
    ///         they declare as a success response — so everything else is here instead of in six
    ///         near-identical copies. It also stores the processors the endpoint will run, which is
    ///         why no tier can register them on its plan and then not run them: both come from
    ///         <paramref name="processors" />.
    ///     </para>
    ///     <para>
    ///         The declarations are explicit because a <c>RequestDelegate</c>-shaped endpoint infers
    ///         nothing, and the request body is narrowed by the verb: a bodyless verb declares no body
    ///         whatever the binding reads (docs/known-issues/067), while a body-carrying one owes the
    ///         declaration so routing can answer <c>415</c> on a wrong content type rather than
    ///         letting the binder answer <c>400</c> (docs/known-issues/065). The <c>400</c> is
    ///         declared as a validation problem, not a plain one, because a binding failure answers
    ///         with <c>HttpValidationProblemDetails</c> and its errors dictionary
    ///         (docs/known-issues/055).
    ///     </para>
    /// </remarks>
    private protected RawEndpointPlan BuildPlan(string route,
        string[] httpMethods,
        EndpointProcessors processors,
        ProducesResponseMetadata response,
        Action<RouteHandlerBuilder> applyConfigured)
    {
        _configuredProcessors = processors;

        return new RawEndpointPlan
        {
            Route = route,
            HttpMethods = httpMethods,
            Processors = processors,
            ApplyMetadata = handlerBuilder =>
            {
                RequestBodyMetadata.Apply(handlerBuilder, DeclaredRequestBody(httpMethods),
                    typeof(TRequest), DeclaredFormFields());

                // Attached as one entry so a single GetMetadata call retrieves the whole list —
                // see BoundParametersMetadata's remarks.
                if (DeclaredParameters() is { Count: > 0 } parameters)
                {
                    handlerBuilder.WithMetadata(new BoundParametersMetadata(parameters));
                }

                handlerBuilder.WithMetadata(response);
                handlerBuilder.ProducesValidationProblem();
                applyConfigured(handlerBuilder);
            }
        };
    }
}
