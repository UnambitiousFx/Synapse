using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Propagation;

/// <summary>
///     An <see cref="IPropagationCarrier" /> over a plain string dictionary.
/// </summary>
/// <remarks>
///     This is the carrier for message transports. Broker client libraries all expose message headers or
///     application properties as string key/value pairs, so a per-broker integration only needs a thin adapter
///     onto this shape rather than its own propagation logic. It is also the natural carrier for outbox
///     entries, whose headers are stored rather than sent.
/// </remarks>
public sealed class DictionaryPropagationCarrier : IPropagationCarrier
{
    private readonly IDictionary<string, string> _headers;

    /// <summary>
    ///     Wraps an existing header dictionary.
    /// </summary>
    /// <param name="headers">The dictionary to read from and write to.</param>
    public DictionaryPropagationCarrier(IDictionary<string, string> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        _headers = headers;
    }

    /// <summary>
    ///     Creates a carrier over a new, empty header dictionary.
    /// </summary>
    public DictionaryPropagationCarrier()
        : this(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
    {
    }

    /// <summary>
    ///     Gets the underlying headers.
    /// </summary>
    public IReadOnlyDictionary<string, string> Headers =>
        _headers as IReadOnlyDictionary<string, string> ??
        new Dictionary<string, string>(_headers, StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public bool TryGetValue(string key,
        out string? value)
    {
        return _headers.TryGetValue(key, out value);
    }

    /// <inheritdoc />
    public void Set(string key,
        string value)
    {
        _headers[key] = value;
    }
}
