namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     A transport-agnostic view over the header-like key/value slots of a message or request.
/// </summary>
/// <remarks>
///     Implementations adapt one transport — an HTTP request, an outgoing <c>HttpRequestMessage</c>, a broker
///     message's application properties — so that propagation logic can be written once and reused for all of
///     them.
/// </remarks>
public interface IPropagationCarrier
{
    /// <summary>
    ///     Reads a single value for the specified key.
    /// </summary>
    /// <param name="key">The header name.</param>
    /// <param name="value">When this method returns, the value if exactly one was present; otherwise <c>null</c>.</param>
    /// <returns><c>true</c> when exactly one value was present; otherwise <c>false</c>.</returns>
    /// <remarks>
    ///     A key carrying several values is reported as absent. Choosing between conflicting values would be
    ///     arbitrary, and propagated state is only meaningful when unambiguous.
    /// </remarks>
    bool TryGetValue(string key,
        out string? value);

    /// <summary>
    ///     Writes a value, replacing any existing value for the key.
    /// </summary>
    /// <param name="key">The header name.</param>
    /// <param name="value">The value to write.</param>
    void Set(string key,
        string value);
}
