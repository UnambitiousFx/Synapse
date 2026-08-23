using Microsoft.AspNetCore.Http;

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
}
