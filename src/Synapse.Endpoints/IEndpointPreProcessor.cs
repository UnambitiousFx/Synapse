using Microsoft.AspNetCore.Http;

namespace UnambitiousFx.Synapse.Endpoints;

/// <summary>
///     Runs before an endpoint binds its request, and may answer the request itself.
/// </summary>
/// <remarks>
///     <para>
///         Registered per endpoint through <c>PreProcessor&lt;T&gt;()</c> on the endpoint builder and
///         resolved from <see cref="HttpContext.RequestServices" /> on every request, so a processor
///         may take scoped dependencies through its constructor — unlike the endpoint itself, which is
///         a singleton.
///     </para>
///     <para>
///         Deliberately shaped on the <see cref="HttpContext" /> rather than on the bound message: it
///         runs before binding, so there is no message yet, and running first is what lets a rejection
///         avoid the cost of deserializing a body it is about to discard. Code that needs the typed
///         message belongs in the endpoint's own <c>OnBeforeHandleAsync</c> override.
///     </para>
/// </remarks>
public interface IEndpointPreProcessor
{
    /// <summary>Inspects the request, optionally answering it without reaching the endpoint.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="cancellationToken">Cancellation token, tied to the request.</param>
    /// <returns>
    ///     A result to write instead of handling the request, or <see langword="null" /> to carry on.
    ///     Returning a result skips binding and dispatch, but not the endpoint's
    ///     <c>OnAfterHandleAsync</c> or any registered <see cref="IEndpointPostProcessor" />.
    /// </returns>
    ValueTask<IResult?> ProcessAsync(HttpContext context,
        CancellationToken cancellationToken);
}
