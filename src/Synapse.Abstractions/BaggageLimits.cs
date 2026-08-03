using System.Text;

namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Limits and validation rules for context baggage, matching the W3C Baggage specification.
/// </summary>
/// <remarks>
///     <para>
///         Baggage crosses process boundaries as the <c>baggage</c> HTTP header (or the equivalent message
///         header), so it is subject to the W3C size limits. Intermediaries are permitted to silently
///         truncate baggage that exceeds them, which is why Synapse enforces the limits itself and reports
///         a rejected entry to the caller rather than emitting a header that may be mangled downstream.
///     </para>
///     <para>
///         Baggage is visible to every downstream service, including third parties outside your control.
///         Never place confidential values in it.
///     </para>
/// </remarks>
public static class BaggageLimits
{
    /// <summary>
    ///     The maximum number of baggage entries a context may carry.
    /// </summary>
    public const int MaxEntryCount = 64;

    /// <summary>
    ///     The maximum total size, in bytes, of the serialized baggage.
    /// </summary>
    public const int MaxTotalBytes = 8192;

    /// <summary>
    ///     Determines whether the specified string is a valid baggage key.
    /// </summary>
    /// <param name="key">The candidate key.</param>
    /// <returns><c>true</c> when the key can be serialized into a <c>baggage</c> header; otherwise <c>false</c>.</returns>
    public static bool IsValidKey(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        foreach (var c in key)
        {
            // ',' and '=' are the baggage list and key/value delimiters; control characters and
            // whitespace cannot appear in a header token.
            if (c is ',' or '=' || char.IsWhiteSpace(c) || char.IsControl(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     Determines whether the specified string is a valid baggage value.
    /// </summary>
    /// <param name="value">The candidate value.</param>
    /// <returns><c>true</c> when the value can be serialized into a <c>baggage</c> header; otherwise <c>false</c>.</returns>
    /// <remarks>
    ///     Only control characters are refused. Delimiters are not: a value is percent-encoded on the wire, so a
    ///     comma or an equals sign in it is escaped rather than ambiguous. Refusing them here rejected values the
    ///     specification allows and silently dropped inbound entries a conformant peer had escaped correctly
    ///     (see known issue 038). Keys are a different matter — they travel unescaped, so
    ///     <see cref="IsValidKey" /> is stricter.
    /// </remarks>
    public static bool IsValidValue(string? value)
    {
        if (value is null)
        {
            return false;
        }

        foreach (var c in value)
        {
            if (char.IsControl(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     Measures the serialized size, in bytes, that the specified entry contributes to the baggage header.
    /// </summary>
    /// <param name="key">The entry key.</param>
    /// <param name="value">The entry value.</param>
    /// <returns>The number of bytes the entry occupies, including its delimiters.</returns>
    /// <remarks>
    ///     The value is measured as it appears on the wire, percent-encoded, because that is the form the 8192-byte
    ///     limit governs. Measuring the decoded value understated every entry needing escapes, so baggage that
    ///     passed the check could still exceed the limit on the wire and be truncated by an intermediary
    ///     (see known issue 039).
    /// </remarks>
    public static int MeasureEntry(string key,
        string value)
    {
        // "key=value" plus the "," that separates it from the preceding entry.
        return Encoding.UTF8.GetByteCount(key) + MeasureEncodedValue(value) + 2;
    }

    /// <summary>
    ///     Measures the length, in bytes, of the percent-encoded form of a baggage value.
    /// </summary>
    /// <param name="value">The decoded value.</param>
    /// <returns>The number of bytes <c>Uri.EscapeDataString</c> would produce.</returns>
    /// <remarks>
    ///     Counted rather than encoded, so measuring allocates nothing. Escaping leaves the RFC 3986 unreserved
    ///     characters alone — each one byte — and expands every other byte to a three-character escape, so the
    ///     encoded length follows from the UTF-8 byte count and the number of unreserved characters.
    /// </remarks>
    public static int MeasureEncodedValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var totalBytes = Encoding.UTF8.GetByteCount(value);
        var unreserved = 0;

        foreach (var c in value)
        {
            if (IsUnreserved(c))
            {
                unreserved++;
            }
        }

        return unreserved + ((totalBytes - unreserved) * 3);
    }

    private static bool IsUnreserved(char c)
    {
        return c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '.' or '_' or '~';
    }
}
