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
    /// <summary>
    ///     The key body failures are reported under. A body that cannot be read is reported alone
    ///     rather than alongside route or query failures: without a deserialized message there is
    ///     nothing left to bind the remaining values onto.
    /// </summary>
    public const string BodyField = "body";

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
    /// <param name="cancellationToken">
    ///     An additional token to cancel the read by, linked with
    ///     <see cref="HttpContext.RequestAborted" />. Generated binders pass none.
    /// </param>
    /// <returns>The deserialized body, or a failure describing what was wrong with it.</returns>
    /// <remarks>
    ///     Called by generated binders. Every way the body can be unusable — absent, not JSON, or
    ///     malformed JSON — returns a failure the endpoint turns into a 400, never an exception: an
    ///     unhandled exception here would be a 500 for what is a client mistake.
    /// </remarks>
    public static async ValueTask<BindResult<T>> ReadJsonBodyAsync<T>(HttpContext context,
        CancellationToken cancellationToken = default)
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
            return BindResult<T>.Failure(BodyField, "The request body is required but was empty or null.");
        }

        // ReadFromJsonAsync throws InvalidOperationException — not JsonException — when the content
        // type is not a JSON one, and the catch below only covers JsonException, so a body sent with
        // no Content-Type header at all used to escape as a 500 plus an "An unhandled exception was
        // thrown" log line per request. A wrong-but-present content type (text/plain) never gets
        // this far: the Accepts-driven consumes matcher policy rejects it with 415 during routing.
        // An *absent* content type matches any endpoint regardless of what it declares it accepts,
        // which is exactly why this is the one shape that reaches here.
        if (!context.Request.HasJsonContentType())
        {
            return BindResult<T>.Failure(
                BodyField,
                "The request body is required to be JSON, but the request declared content type " +
                $"'{context.Request.ContentType ?? string.Empty}'. Send the body as application/json.");
        }

        // Keyed on this application's JsonSerializerOptions instance, not held in a static generic
        // holder. A static holder was one cache per closed T per *process*, not per application: two
        // hosts in the same process with different ConfigureHttpJsonOptions silently shared whichever
        // one resolved first, and ReadFromJsonAsync serializes with the type info's own options, so
        // the second host's configuration was ignored outright. See HttpJsonTypeInfo for why the
        // cache is kept rather than calling options.GetTypeInfo per request.
        var typeInfo = HttpJsonTypeInfo.Resolve<T>(context);

        // The read is always bound to the request's own lifetime, and to the caller's token as well
        // when they supplied a distinct one — a linked source is allocated only in that case, so the
        // generated binders, which pass no token, still allocate nothing here.
        CancellationTokenSource? linked = null;
        var effective = context.RequestAborted;

        if (cancellationToken.CanBeCanceled && cancellationToken != context.RequestAborted)
        {
            linked = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, cancellationToken);
            effective = linked.Token;
        }

        try
        {
            var value = await context.Request.ReadFromJsonAsync(typeInfo, effective);

            return value is null
                ? BindResult<T>.Failure(BodyField, "The request body is required but was empty or null.")
                : BindResult<T>.Success(value);
        }
        catch (JsonException exception)
        {
            return BindResult<T>.Failure(BodyField, $"The request body is not valid JSON: {exception.Message}");
        }
        finally
        {
            linked?.Dispose();
        }
    }
}
