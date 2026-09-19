using Microsoft.AspNetCore.Http;

namespace UnambitiousFx.Synapse.Endpoints;

/// <summary>
///     Runs on the way out, after the endpoint has produced a result and before it is written.
/// </summary>
/// <remarks>
///     <para>
///         Registered per endpoint through <c>PostProcessor&lt;T&gt;()</c> on the endpoint builder and
///         resolved from <see cref="HttpContext.RequestServices" /> on every request.
///     </para>
///     <para>
///         Runs whatever produced a result — a pre-processor's short circuit, a binding failure's
///         <c>400</c>, a mapped dispatch failure, or the success mapper. That is what makes it usable
///         for response headers: a correlation header that skipped every <c>400</c> would be a bug,
///         not an optimisation. It does <em>not</em> run when dispatch, a mapper, or a hook throws:
///         that exception bypasses every post-processor and reaches the ASP.NET exception handler
///         instead, unwritten headers included.
///     </para>
///     <para>
///         The result has not been executed yet, so writing to
///         <see cref="HttpResponse.Headers" /> here still reaches the wire — but
///         <see cref="HttpResponse.StatusCode" /> is still whatever it defaulted to, because nothing
///         has written the result either. Reading the status a result is about to write means
///         pattern-matching it:
///         <code>
///     var status = result is IStatusCodeHttpResult s ? s.StatusCode : null;
///         </code>
///         which is unavailable for a result that does not implement
///         <see cref="IStatusCodeHttpResult" />, including the stream tier's negotiated writer.
///     </para>
/// </remarks>
public interface IEndpointPostProcessor
{
    /// <summary>Inspects or replaces the result the endpoint produced.</summary>
    /// <param name="result">The result the endpoint produced, or the previous processor returned.</param>
    /// <param name="context">The HTTP context.</param>
    /// <param name="cancellationToken">Cancellation token, tied to the request.</param>
    /// <returns>The result to write. Return <paramref name="result" /> to leave it unchanged.</returns>
    ValueTask<IResult> ProcessAsync(IResult result,
        HttpContext context,
        CancellationToken cancellationToken);
}
