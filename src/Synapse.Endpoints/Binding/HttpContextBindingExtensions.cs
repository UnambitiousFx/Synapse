using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace UnambitiousFx.Synapse.Endpoints.Binding;

/// <summary>
///     The low level's helper surface: everything a hand-written <c>HandleAsync</c> needs to read a
///     request, as extension methods on <see cref="HttpContext" />.
/// </summary>
/// <remarks>
///     <para>
///         These delegate to <see cref="BindingHelpers" />, which is also the only thing generated
///         binders ever call. That is what makes "the high level is built on the low level" a fact
///         about the code rather than a claim in the documentation: both levels read the request
///         through the same primitives.
///     </para>
///     <para>
///         Typed readers parse with <see cref="CultureInfo.InvariantCulture" />, matching ASP.NET
///         Core's own parameter binding. Enum readers are named separately (<c>…Enum</c>) rather than
///         overloaded, because two generic methods differing only in their constraints are a duplicate
///         signature to the compiler. The <c>…Optional</c> readers do overload, one
///         <see langword="struct" />-constrained and one <see langword="class" />-constrained: their
///         <see langword="out" /> parameter is <c>Nullable&lt;T&gt;</c> in the one case and <c>T</c> in
///         the other, so the signatures genuinely differ.
///     </para>
///     <para>
///         The plain readers answer <see langword="false" /> to both an absent value and an invalid
///         one. The <c>…Optional</c> readers separate them: absent is <see langword="true" /> with a
///         <see langword="null" /> value, and only a present-but-unparsable value is
///         <see langword="false" />. That is the same contract
///         <see cref="BindingValidator" />'s optional readers have, minus the error collection.
///     </para>
///     <para>
///         Being extension methods rather than a wrapper type, they are usable anywhere a
///         <see cref="HttpContext" /> is — middleware, endpoint filters, a minimal-API lambda — and
///         they allocate nothing.
///     </para>
/// </remarks>
public static class HttpContextBindingExtensions
{
    /// <summary>Reads a route value as a string.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="name">The route parameter name.</param>
    /// <param name="value">The raw value when present.</param>
    /// <returns><see langword="true" /> when the route parameter was present and non-null.</returns>
    public static bool TryGetRoute(this HttpContext context,
        string name,
        out string? value)
    {
        ArgumentNullException.ThrowIfNull(context);
        return BindingHelpers.TryGetRoute(context, name, out value);
    }

    /// <summary>Reads a query value as a string, taking the first when repeated.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="name">The query key.</param>
    /// <param name="value">The raw value when present.</param>
    /// <returns><see langword="true" /> when the key was present.</returns>
    public static bool TryGetQuery(this HttpContext context,
        string name,
        out string? value)
    {
        ArgumentNullException.ThrowIfNull(context);
        return BindingHelpers.TryGetQuery(context, name, out value);
    }

    /// <summary>Reads a header as a string, taking the first when repeated.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="name">The header name.</param>
    /// <param name="value">The raw value when present.</param>
    /// <returns><see langword="true" /> when the header was present.</returns>
    public static bool TryGetHeader(this HttpContext context,
        string name,
        out string? value)
    {
        ArgumentNullException.ThrowIfNull(context);
        return BindingHelpers.TryGetHeader(context, name, out value);
    }

    /// <summary>Reads and parses a route value.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="context">The HTTP context.</param>
    /// <param name="name">The route parameter name.</param>
    /// <param name="value">The parsed value, or <see langword="default" /> when absent or unparsable.</param>
    /// <returns><see langword="true" /> when the value was present and parsed.</returns>
    public static bool TryGetRoute<T>(this HttpContext context,
        string name,
        out T value)
        where T : IParsable<T>
    {
        return TryParse(context.TryGetRoute(name, out var raw), raw, out value);
    }

    /// <summary>Reads and parses a query value, taking the first when repeated.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="context">The HTTP context.</param>
    /// <param name="name">The query key.</param>
    /// <param name="value">The parsed value, or <see langword="default" /> when absent or unparsable.</param>
    /// <returns><see langword="true" /> when the value was present and parsed.</returns>
    public static bool TryGetQuery<T>(this HttpContext context,
        string name,
        out T value)
        where T : IParsable<T>
    {
        return TryParse(context.TryGetQuery(name, out var raw), raw, out value);
    }

    /// <summary>Reads and parses a header, taking the first when repeated.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="context">The HTTP context.</param>
    /// <param name="name">The header name.</param>
    /// <param name="value">The parsed value, or <see langword="default" /> when absent or unparsable.</param>
    /// <returns><see langword="true" /> when the value was present and parsed.</returns>
    public static bool TryGetHeader<T>(this HttpContext context,
        string name,
        out T value)
        where T : IParsable<T>
    {
        return TryParse(context.TryGetHeader(name, out var raw), raw, out value);
    }

    /// <summary>Reads an optional route value, treating an absent one as success.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="context">The HTTP context.</param>
    /// <param name="name">The route parameter name.</param>
    /// <param name="value">The parsed value, or <see langword="null" /> when absent.</param>
    /// <returns><see langword="true" /> when the value was absent or present and parsed.</returns>
    public static bool TryGetRouteOptional<T>(this HttpContext context,
        string name,
        out T? value)
        where T : struct, IParsable<T>
    {
        return TryParseOptional(context.TryGetRoute(name, out var raw), raw, out value);
    }

    /// <summary>Reads an optional route value of a reference type, treating an absent one as success.</summary>
    /// <typeparam name="T">The reference type to parse into.</typeparam>
    /// <param name="context">The HTTP context.</param>
    /// <param name="name">The route parameter name.</param>
    /// <param name="value">The parsed value, or <see langword="null" /> when absent.</param>
    /// <returns><see langword="true" /> when the value was absent or present and parsed.</returns>
    public static bool TryGetRouteOptional<T>(this HttpContext context,
        string name,
        out T? value)
        where T : class, IParsable<T>
    {
        return TryParseOptional(context.TryGetRoute(name, out var raw), raw, out value);
    }

    /// <summary>Reads an optional query value, treating an absent one as success.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="context">The HTTP context.</param>
    /// <param name="name">The query key.</param>
    /// <param name="value">The parsed value, or <see langword="null" /> when absent.</param>
    /// <returns><see langword="true" /> when the value was absent or present and parsed.</returns>
    public static bool TryGetQueryOptional<T>(this HttpContext context,
        string name,
        out T? value)
        where T : struct, IParsable<T>
    {
        return TryParseOptional(context.TryGetQuery(name, out var raw), raw, out value);
    }

    /// <summary>Reads an optional query value of a reference type, treating an absent one as success.</summary>
    /// <typeparam name="T">The reference type to parse into.</typeparam>
    /// <param name="context">The HTTP context.</param>
    /// <param name="name">The query key.</param>
    /// <param name="value">The parsed value, or <see langword="null" /> when absent.</param>
    /// <returns><see langword="true" /> when the value was absent or present and parsed.</returns>
    public static bool TryGetQueryOptional<T>(this HttpContext context,
        string name,
        out T? value)
        where T : class, IParsable<T>
    {
        return TryParseOptional(context.TryGetQuery(name, out var raw), raw, out value);
    }

    /// <summary>Reads an optional header, treating an absent one as success.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="context">The HTTP context.</param>
    /// <param name="name">The header name.</param>
    /// <param name="value">The parsed value, or <see langword="null" /> when absent.</param>
    /// <returns><see langword="true" /> when the value was absent or present and parsed.</returns>
    public static bool TryGetHeaderOptional<T>(this HttpContext context,
        string name,
        out T? value)
        where T : struct, IParsable<T>
    {
        return TryParseOptional(context.TryGetHeader(name, out var raw), raw, out value);
    }

    /// <summary>Reads an optional header of a reference type, treating an absent one as success.</summary>
    /// <typeparam name="T">The reference type to parse into.</typeparam>
    /// <param name="context">The HTTP context.</param>
    /// <param name="name">The header name.</param>
    /// <param name="value">The parsed value, or <see langword="null" /> when absent.</param>
    /// <returns><see langword="true" /> when the value was absent or present and parsed.</returns>
    public static bool TryGetHeaderOptional<T>(this HttpContext context,
        string name,
        out T? value)
        where T : class, IParsable<T>
    {
        return TryParseOptional(context.TryGetHeader(name, out var raw), raw, out value);
    }

    /// <summary>Reads a route value as an enum, accepting its name or its numeric value.</summary>
    /// <typeparam name="TEnum">The enum type.</typeparam>
    /// <param name="context">The HTTP context.</param>
    /// <param name="name">The route parameter name.</param>
    /// <param name="value">The parsed value.</param>
    /// <returns><see langword="true" /> when the value was present and parsed.</returns>
    public static bool TryGetRouteEnum<TEnum>(this HttpContext context,
        string name,
        out TEnum value)
        where TEnum : struct, Enum
    {
        return TryParseEnum(context.TryGetRoute(name, out var raw), raw, out value);
    }

    /// <summary>Reads a query value as an enum, accepting its name or its numeric value.</summary>
    /// <typeparam name="TEnum">The enum type.</typeparam>
    /// <param name="context">The HTTP context.</param>
    /// <param name="name">The query key.</param>
    /// <param name="value">The parsed value.</param>
    /// <returns><see langword="true" /> when the value was present and parsed.</returns>
    public static bool TryGetQueryEnum<TEnum>(this HttpContext context,
        string name,
        out TEnum value)
        where TEnum : struct, Enum
    {
        return TryParseEnum(context.TryGetQuery(name, out var raw), raw, out value);
    }

    /// <summary>Reads a header as an enum, accepting its name or its numeric value.</summary>
    /// <typeparam name="TEnum">The enum type.</typeparam>
    /// <param name="context">The HTTP context.</param>
    /// <param name="name">The header name.</param>
    /// <param name="value">The parsed value.</param>
    /// <returns><see langword="true" /> when the value was present and parsed.</returns>
    public static bool TryGetHeaderEnum<TEnum>(this HttpContext context,
        string name,
        out TEnum value)
        where TEnum : struct, Enum
    {
        return TryParseEnum(context.TryGetHeader(name, out var raw), raw, out value);
    }

    /// <summary>Reads a header, returning <see langword="null" /> when it is absent.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="name">The header name.</param>
    /// <returns>The first value, or <see langword="null" />.</returns>
    public static string? Header(this HttpContext context,
        string name)
    {
        return context.TryGetHeader(name, out var value) ? value : null;
    }

    /// <summary>
    ///     Reads every value of a repeated query key.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="name">The query key.</param>
    /// <returns>All values, empty when the key is absent.</returns>
    /// <remarks>
    ///     The single-value readers take the first value when a key repeats, which is what convention
    ///     binding needs. A hand-written handler wanting <c>?tag=a&amp;tag=b</c> needs all of them.
    /// </remarks>
    public static StringValues QueryValues(this HttpContext context,
        string name)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Request.Query.TryGetValue(name, out var values) ? values : StringValues.Empty;
    }

    /// <summary>Reads every value of a repeated header.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="name">The header name.</param>
    /// <returns>All values, empty when the header is absent.</returns>
    /// <remarks>
    ///     The single-value readers take the first value when a header repeats, which is what
    ///     convention binding needs. A hand-written handler wanting all of them needs this.
    /// </remarks>
    public static StringValues HeaderValues(this HttpContext context,
        string name)
    {
        ArgumentNullException.ThrowIfNull(context);
        return BindingHelpers.TryGetHeaderValues(context, name, out var values) ? values : StringValues.Empty;
    }

    /// <summary>
    ///     Reads and deserializes the JSON request body using the application's configured JSON options.
    /// </summary>
    /// <typeparam name="T">The body type.</typeparam>
    /// <param name="context">The HTTP context.</param>
    /// <param name="cancellationToken">
    ///     An additional token to cancel the read by. Linked with
    ///     <see cref="HttpContext.RequestAborted" />, which always applies, so passing
    ///     <see langword="default" /> is the same as passing the request's own token.
    /// </param>
    /// <returns>The deserialized body, or a failure keyed <c>body</c> describing what was wrong with it.</returns>
    /// <remarks>
    ///     Every way the body can be unusable — absent, not JSON, or malformed — is a failure rather
    ///     than an exception, so a client mistake stays a <c>400</c> instead of becoming a <c>500</c>.
    ///     Under Native AOT <typeparamref name="T" /> must be registered on a
    ///     <c>JsonSerializerContext</c>; SYNE008 checks the call sites it can see.
    /// </remarks>
    public static ValueTask<BindResult<T>> BodyAsync<T>(this HttpContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return BindingHelpers.ReadJsonBodyAsync<T>(context, cancellationToken);
    }

    /// <summary>Reads the request form, so the synchronous form readers can serve from its cache.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="cancellationToken">
    ///     An additional token to cancel the read by. Linked with
    ///     <see cref="HttpContext.RequestAborted" />, which always applies, so passing
    ///     <see langword="default" /> is the same as passing the request's own token.
    /// </param>
    /// <returns>The parsed form, or a failure keyed <c>body</c> describing what was wrong with it.</returns>
    /// <remarks>
    ///     Every way the body can be unusable — no <c>Content-Type</c>, or a malformed multipart body —
    ///     is a failure rather than an exception, so a client mistake stays a <c>400</c> instead of
    ///     becoming a <c>500</c>. Call this before any of the synchronous form readers below.
    /// </remarks>
    public static ValueTask<BindResult<IFormCollection>> FormAsync(this HttpContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return BindingHelpers.ReadFormAsync(context, cancellationToken);
    }

    /// <summary>Reads a form field as a string, taking the first when repeated.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="name">The field name.</param>
    /// <param name="value">The raw value when present.</param>
    /// <returns><see langword="true" /> when the field was present.</returns>
    /// <remarks>Requires that the form has already been read — see <see cref="FormAsync" />.</remarks>
    public static bool TryGetForm(this HttpContext context,
        string name,
        out string? value)
    {
        ArgumentNullException.ThrowIfNull(context);
        return BindingHelpers.TryGetForm(context, name, out value);
    }

    /// <summary>Reads one uploaded file.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="name">The field name the file was uploaded under.</param>
    /// <param name="file">The file when present.</param>
    /// <returns><see langword="true" /> when a file was uploaded under that name.</returns>
    /// <remarks>Requires that the form has already been read — see <see cref="FormAsync" />.</remarks>
    public static bool TryGetFormFile(this HttpContext context,
        string name,
        out IFormFile? file)
    {
        ArgumentNullException.ThrowIfNull(context);
        return BindingHelpers.TryGetFormFile(context, name, out file);
    }

    /// <summary>Reads every value of a repeated form field.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="name">The field name.</param>
    /// <returns>All values, empty when the field is absent.</returns>
    /// <remarks>
    ///     The single-value reader takes the first value when a field repeats, which is what
    ///     convention binding needs. A hand-written handler wanting all of them needs this. Requires
    ///     that the form has already been read — see <see cref="FormAsync" />.
    /// </remarks>
    public static StringValues FormValues(this HttpContext context,
        string name)
    {
        ArgumentNullException.ThrowIfNull(context);
        return BindingHelpers.TryGetFormValues(context, name, out var values) ? values : StringValues.Empty;
    }

    /// <summary>Reads every file uploaded under one field name.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="name">The field name.</param>
    /// <returns>Every file under that name, empty when there are none.</returns>
    /// <remarks>
    ///     <see cref="IFormFileCollection" /> means every file on the request; this means the files
    ///     under one field name. Requires that the form has already been read — see
    ///     <see cref="FormAsync" />.
    /// </remarks>
    public static IReadOnlyList<IFormFile> FormFiles(this HttpContext context,
        string name)
    {
        ArgumentNullException.ThrowIfNull(context);
        return BindingHelpers.GetFormFiles(context, name);
    }

    /// <summary>Resolves a required service for this request.</summary>
    /// <typeparam name="TService">The service type.</typeparam>
    /// <param name="context">The HTTP context.</param>
    /// <returns>The service.</returns>
    /// <remarks>
    ///     Endpoints are startup-created singletons with no constructor injection, so this is how a
    ///     handler reaches its dependencies.
    /// </remarks>
    public static TService Service<TService>(this HttpContext context)
        where TService : notnull
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.RequestServices.GetRequiredService<TService>();
    }

    /// <summary>Starts collecting binding errors for this request.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>A fresh validator.</returns>
    /// <remarks>
    ///     The returned value is a mutable struct: assign it to a local and call into that local. See
    ///     <see cref="BindingValidator" /> for why.
    /// </remarks>
    public static BindingValidator Validate(this HttpContext context)
    {
        return new BindingValidator(context);
    }

    private static bool TryParse<T>(bool present,
        string? raw,
        out T value)
        where T : IParsable<T>
    {
        if (present &&
            T.TryParse(raw, CultureInfo.InvariantCulture, out var parsed))
        {
            value = parsed;
            return true;
        }

        value = default!;
        return false;
    }

    private static bool TryParseOptional<T>(bool present,
        string? raw,
        out T? value)
        where T : struct, IParsable<T>
    {
        value = null;

        if (!present)
        {
            return true;
        }

        if (!T.TryParse(raw, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryParseOptional<T>(bool present,
        string? raw,
        out T? value)
        where T : class, IParsable<T>
    {
        value = null;

        if (!present)
        {
            return true;
        }

        if (!T.TryParse(raw, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryParseEnum<TEnum>(bool present,
        string? raw,
        out TEnum value)
        where TEnum : struct, Enum
    {
        if (present &&
            Enum.TryParse(raw, out value))
        {
            return true;
        }

        value = default;
        return false;
    }
}
