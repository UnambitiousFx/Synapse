using Microsoft.AspNetCore.Http;

namespace UnambitiousFx.Synapse.Endpoints.Binding;

/// <summary>
///     Builds a message from an HTTP request. Implementations are emitted by the
///     Synapse.Endpoints analyzer, one per message type, and assign properties directly so that
///     no reflection is needed at request time.
/// </summary>
/// <typeparam name="TRequest">The message type.</typeparam>
public interface IEndpointBinder<TRequest>
{
    /// <summary>Binds the incoming request onto a new message instance.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>The bound message, or a failure describing what could not be bound.</returns>
    ValueTask<BindResult<TRequest>> BindAsync(HttpContext context);

    /// <summary>
    ///     Whether <see cref="BindAsync" /> deserializes the request body, so the endpoint can
    ///     declare what it accepts to match.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Generated binders report this exactly: a message is deserialized only when some
    ///         property binds from the body, which the endpoint itself cannot work out — it knows the
    ///         verb, not what each property bound from. Declaring <c>Accepts</c> from the verb alone
    ///         put a request schema on endpoints that read nothing and made them reject a content type
    ///         they never look at; see <c>docs/known-issues/067</c>.
    ///     </para>
    ///     <para>
    ///         Defaults to <see langword="true" /> so a hand-written binder written before this member
    ///         existed keeps its current behaviour: it may read the body, and declaring that it might
    ///         is the safe answer.
    ///     </para>
    /// </remarks>
    bool ReadsRequestBody => true;
}
