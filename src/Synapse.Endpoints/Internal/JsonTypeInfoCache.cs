using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace UnambitiousFx.Synapse.Endpoints.Internal;

/// <summary>
///     Resolves and caches the <see cref="JsonTypeInfo{T}" /> for one type from the application's
///     configured resolver chain.
/// </summary>
/// <typeparam name="T">The serialized type.</typeparam>
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
        if (_typeInfo is not null)
        {
            return _typeInfo;
        }

        var options = context.RequestServices
            .GetRequiredService<IOptions<JsonOptions>>()
            .Value.SerializerOptions;

        // GetTypeInfo requires the options to be sealed first.
        options.MakeReadOnly();

        return _typeInfo = (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));
    }
}
