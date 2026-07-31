using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Propagation;

/// <summary>
///     An <see cref="IPropagationCarrier" /> over the headers of an outgoing <see cref="HttpRequestMessage" />.
/// </summary>
public sealed class HttpRequestMessagePropagationCarrier : IPropagationCarrier
{
    private readonly HttpRequestMessage _request;

    /// <summary>
    ///     Wraps an outgoing request.
    /// </summary>
    /// <param name="request">The request whose headers should carry the propagated state.</param>
    public HttpRequestMessagePropagationCarrier(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _request = request;
    }

    /// <inheritdoc />
    public bool TryGetValue(string key,
        out string? value)
    {
        value = null;

        if (!_request.Headers.TryGetValues(key, out var values))
        {
            return false;
        }

        using var enumerator = values.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            return false;
        }

        var single = enumerator.Current;
        if (enumerator.MoveNext())
        {
            // Several values for one key is ambiguous; report absent rather than guess.
            return false;
        }

        value = single;
        return true;
    }

    /// <inheritdoc />
    public void Set(string key,
        string value)
    {
        _request.Headers.Remove(key);
        _request.Headers.TryAddWithoutValidation(key, value);
    }
}
