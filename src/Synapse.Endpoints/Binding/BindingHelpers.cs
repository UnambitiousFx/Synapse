using System.Text.Json;
using Microsoft.AspNetCore.Http;
using UnambitiousFx.Synapse.Endpoints.Internal;

namespace UnambitiousFx.Synapse.Endpoints.Binding;

/// <summary>
///     Primitive readers used by generated binders. All of them are plain string access with no
///     binding machinery, so they carry no trimming or AOT annotations.
/// </summary>
public static class BindingHelpers
{
    /// <summary>Reads a route value.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="name">The route parameter name.</param>
    /// <param name="value">The raw value when present.</param>
    /// <returns><see langword="true" /> when the route parameter was present and non-null.</returns>
    public static bool TryGetRoute(HttpContext context,
        string name,
        out string? value)
    {
        if (context.Request.RouteValues.TryGetValue(name, out var raw) &&
            raw is not null)
        {
            value = raw.ToString();
            return value is not null;
        }

        value = null;
        return false;
    }

    /// <summary>Reads a query-string value, taking the first when repeated.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="name">The query key.</param>
    /// <param name="value">The raw value when present.</param>
    /// <returns><see langword="true" /> when the key was present.</returns>
    public static bool TryGetQuery(HttpContext context,
        string name,
        out string? value)
    {
        if (context.Request.Query.TryGetValue(name, out var raw) &&
            raw.Count > 0)
        {
            value = raw[0];
            return value is not null;
        }

        value = null;
        return false;
    }

    /// <summary>Reads a header value, taking the first when repeated.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="name">The header name.</param>
    /// <param name="value">The raw value when present.</param>
    /// <returns><see langword="true" /> when the header was present.</returns>
    public static bool TryGetHeader(HttpContext context,
        string name,
        out string? value)
    {
        if (context.Request.Headers.TryGetValue(name, out var raw) &&
            raw.Count > 0)
        {
            value = raw[0];
            return value is not null;
        }

        value = null;
        return false;
    }

    /// <summary>
    ///     Reads and deserializes the request body using the application's configured JSON options.
    /// </summary>
    /// <typeparam name="T">The body type.</typeparam>
    /// <param name="context">The HTTP context.</param>
    /// <returns>The deserialized body, or a failure describing what was wrong with it.</returns>
    /// <remarks>Called by generated binders. The type info is resolved once per type and cached.</remarks>
    public static async ValueTask<BindResult<T>> ReadJsonBodyAsync<T>(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // A genuinely empty body deserializes to nothing — System.Text.Json does not return null
        // for it, it throws JsonException ("input does not contain any JSON tokens"), which would
        // otherwise surface as "not valid JSON" rather than the more useful "required" message.
        // Checking Content-Length catches the common case (most clients send it, including an
        // explicit 0 for a bodyless request); a chunked request with no Content-Length header and a
        // truly empty stream still falls through to the JsonException branch below, which still
        // reports a sensible (if less specific) failure rather than miscategorizing the body as valid.
        if (context.Request.ContentLength == 0)
        {
            return BindResult<T>.Failure("The request body is required but was empty or null.");
        }

        var typeInfo = BodyTypeInfo<T>.Cache.Get(context);

        try
        {
            var value = await context.Request.ReadFromJsonAsync(typeInfo, context.RequestAborted);

            return value is null
                ? BindResult<T>.Failure("The request body is required but was empty or null.")
                : BindResult<T>.Success(value);
        }
        catch (JsonException exception)
        {
            return BindResult<T>.Failure($"The request body is not valid JSON: {exception.Message}");
        }
    }

    private static class BodyTypeInfo<T>
    {
        internal static readonly JsonTypeInfoCache<T> Cache = new();
    }
}
