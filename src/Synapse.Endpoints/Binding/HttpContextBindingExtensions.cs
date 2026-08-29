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
///         signature to the compiler.
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
