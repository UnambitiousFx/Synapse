using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http;

namespace UnambitiousFx.Synapse.Endpoints.Internal;

/// <summary>
///     Writes a streamed sequence as a JSON array when executed.
/// </summary>
/// <typeparam name="TItem">The streamed item type.</typeparam>
/// <remarks>
///     Streaming is the one endpoint shape that writes the response body itself rather than handing
///     back a value to serialize, so it needs an <see cref="IResult" /> of its own to take part in the
///     single <c>HandleAsync</c> contract every endpoint shares. Execution is deferred to
///     <see cref="ExecuteAsync" />, which is invoked at exactly the point the writer used to be called
///     directly — nothing about the bytes on the wire, the flush cadence or the negotiated headers
///     changes.
/// </remarks>
internal sealed class JsonArrayStreamResult<TItem> : IResult
{
    private readonly IAsyncEnumerable<TItem> _items;
    private readonly JsonTypeInfo<TItem> _typeInfo;

    /// <summary>Initializes a new instance of the <see cref="JsonArrayStreamResult{TItem}" /> class.</summary>
    /// <param name="items">The sequence to write.</param>
    /// <param name="typeInfo">The item's JSON type info.</param>
    internal JsonArrayStreamResult(IAsyncEnumerable<TItem> items,
        JsonTypeInfo<TItem> typeInfo)
    {
        _items = items;
        _typeInfo = typeInfo;
    }

    /// <inheritdoc />
    public Task ExecuteAsync(HttpContext httpContext)
    {
        return StreamResponseWriter.WriteJsonArrayAsync(httpContext, _items, _typeInfo);
    }
}

/// <summary>
///     Writes a streamed sequence as server-sent events when executed.
/// </summary>
/// <typeparam name="TItem">The streamed item type.</typeparam>
/// <remarks>See <see cref="JsonArrayStreamResult{TItem}" /> for why streaming needs its own result type.</remarks>
internal sealed class ServerSentEventsStreamResult<TItem> : IResult
{
    private readonly IAsyncEnumerable<TItem> _items;
    private readonly JsonTypeInfo<TItem> _typeInfo;

    /// <summary>Initializes a new instance of the <see cref="ServerSentEventsStreamResult{TItem}" /> class.</summary>
    /// <param name="items">The sequence to write.</param>
    /// <param name="typeInfo">The item's JSON type info.</param>
    internal ServerSentEventsStreamResult(IAsyncEnumerable<TItem> items,
        JsonTypeInfo<TItem> typeInfo)
    {
        _items = items;
        _typeInfo = typeInfo;
    }

    /// <inheritdoc />
    public Task ExecuteAsync(HttpContext httpContext)
    {
        return StreamResponseWriter.WriteServerSentEventsAsync(httpContext, _items, _typeInfo);
    }
}
