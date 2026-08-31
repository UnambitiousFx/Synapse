using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace UnambitiousFx.Synapse.Endpoints.Internal;

/// <summary>
///     Resolves the <see cref="JsonTypeInfo{T}" /> for one type from the JSON options of the
///     application serving the current request, caching the result <em>per options instance</em>.
/// </summary>
/// <remarks>
///     Use this wherever there is no object with the application's own lifetime to hang a
///     <see cref="JsonTypeInfoCache{T}" /> on — notably from generated binders, which reach
///     <c>BindingHelpers</c> through a static call and whose <c>IEndpointBinder</c> instances live in
///     a process-wide registry rather than per application.
/// </remarks>
internal static class HttpJsonTypeInfo
{
    /// <summary>
    ///     Resolves the type info for <typeparamref name="T" /> from this request's JSON options.
    /// </summary>
    /// <typeparam name="T">The serialized type.</typeparam>
    /// <param name="context">The HTTP context whose services supply the JSON options.</param>
    /// <returns>The type info for <typeparamref name="T" />.</returns>
    internal static JsonTypeInfo<T> Resolve<T>(HttpContext context)
    {
        var options = context.RequestServices
            .GetRequiredService<IOptions<JsonOptions>>()
            .Value.SerializerOptions;

        return Cache<T>.Get(options);
    }

    /// <summary>
    ///     Per-closed-<typeparamref name="T" /> cache, keyed on the <see cref="JsonSerializerOptions" />
    ///     instance the type info was resolved from.
    /// </summary>
    /// <typeparam name="T">The serialized type.</typeparam>
    /// <remarks>
    ///     <para>
    ///         The key is what makes this correct. A bare <c>static JsonTypeInfo&lt;T&gt;</c> would be one
    ///         entry per closed <typeparamref name="T" /> per <em>process</em>, not per application: two
    ///         hosts in one process (two <c>WebApplicationFactory</c> instances in a single test run,
    ///         say) with different <c>ConfigureHttpJsonOptions</c> would silently share whichever one
    ///         resolved first — and <c>ReadFromJsonAsync(request, jsonTypeInfo, …)</c> serializes with
    ///         the type info's <em>own</em> options, so the second host's configuration would be
    ///         ignored outright rather than merely arriving late.
    ///     </para>
    ///     <para>
    ///         A <see cref="ConditionalWeakTable{TKey,TValue}" /> rather than a dictionary so an
    ///         application's options — and the type infos resolved from them — become collectable with
    ///         the application. Its dependent handles tolerate the value referencing the key (a
    ///         <see cref="JsonTypeInfo" /> holds its options), which a strong-valued map would not.
    ///     </para>
    ///     <para>
    ///         The layer is kept rather than calling <c>options.GetTypeInfo</c> per request even though
    ///         the options object caches type infos internally: measured on the endpoint-dispatch
    ///         benchmark's body-reading arm, resolving per request costs roughly 0.4–0.5 us per request
    ///         (about 6–10% of that arm), while this cache measures the same as the process-static one
    ///         it replaces.
    ///     </para>
    /// </remarks>
    private static class Cache<T>
    {
        private static readonly ConditionalWeakTable<JsonSerializerOptions, JsonTypeInfo<T>> Entries = new();

        internal static JsonTypeInfo<T> Get(JsonSerializerOptions options)
        {
            if (Entries.TryGetValue(options, out var cached))
            {
                return cached;
            }

            // GetTypeInfo requires the options to be sealed first.
            options.MakeReadOnly();
            var resolved = (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));

            // A benign race can resolve twice under concurrent first requests; both results are
            // equivalent, so AddOrUpdate rather than a lock on the hot path.
            Entries.AddOrUpdate(options, resolved);
            return resolved;
        }
    }
}

/// <summary>
///     Caches the <see cref="JsonTypeInfo{T}" /> for one type across the requests of one
///     application.
/// </summary>
/// <typeparam name="T">The serialized type.</typeparam>
/// <remarks>
///     Correct only when held in a field whose lifetime is the application's — an endpoint instance's,
///     for example, which is what <c>StreamEndpoint</c> does. It must never be a <c>static</c> of a
///     generic holder; see <see cref="HttpJsonTypeInfo" />, which is the option to reach for when no
///     such per-application field exists.
/// </remarks>
internal sealed class JsonTypeInfoCache<T>
{
    private JsonTypeInfo<T>? _typeInfo;

    /// <summary>
    ///     Gets the type info, resolving it on first use.
    /// </summary>
    /// <param name="context">The HTTP context whose services supply the JSON options.</param>
    /// <returns>The type info for <typeparamref name="T" />.</returns>
    /// <remarks>
    ///     A benign race can resolve twice under concurrent first requests; both results are
    ///     equivalent, so no lock is taken on the hot path.
    /// </remarks>
    internal JsonTypeInfo<T> Get(HttpContext context)
    {
        return _typeInfo ??= HttpJsonTypeInfo.Resolve<T>(context);
    }
}
