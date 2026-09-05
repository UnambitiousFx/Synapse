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

    /// <summary>What this binder reads the request body as, so the endpoint can declare a matching content type.</summary>
    /// <remarks>
    ///     Defaulted <em>from</em> <see cref="ReadsRequestBody" /> rather than the other way round, so a
    ///     hand-written binder that predates this member and overrides only <c>ReadsRequestBody</c> keeps
    ///     behaving exactly as it did. Generated binders declare both explicitly.
    /// </remarks>
    RequestBodyKind BodyKind => ReadsRequestBody ? RequestBodyKind.Json : RequestBodyKind.None;

    /// <summary>The non-body inputs this binder reads, for OpenAPI parameter declaration.</summary>
    /// <remarks>
    ///     <para>
    ///         Empty by default so a hand-written binder written before this member existed keeps
    ///         compiling and keeps its current document, which declares no parameter — the same
    ///         defaulting rationale as <see cref="ReadsRequestBody" /> and <see cref="BodyKind" />.
    ///         Generated binders declare the full list, backed by a <c>static readonly</c> array so
    ///         it is allocated once per message type rather than once per read.
    ///     </para>
    ///     <para>
    ///         An instance member rather than <c>static virtual</c>: every tier holds its binder as an
    ///         <see cref="IEndpointBinder{TRequest}" /> reference obtained from
    ///         <c>EndpointRegistry.GetBinder&lt;TRequest&gt;()</c>, and a <c>static virtual</c> member
    ///         can only be invoked through a generic parameter bound to the concrete implementing
    ///         type, which nothing here has. Read once per endpoint at startup, never on a request
    ///         path, so dispatch cost is not a consideration.
    ///     </para>
    /// </remarks>
    IReadOnlyList<Internal.BoundParameterMetadata> Parameters => [];

    /// <summary>
    ///     The form fields and file parts this binder reads, when <see cref="BodyKind" /> is
    ///     <see cref="RequestBodyKind.Form" />.
    /// </summary>
    /// <remarks>Empty by default, and empty for every non-form binder.</remarks>
    IReadOnlyList<Internal.FormFieldMetadata> FormFields => [];
}
