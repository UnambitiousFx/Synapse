using System.Text;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Propagation;

/// <summary>
///     Serializes and parses the W3C <c>baggage</c> header.
/// </summary>
/// <remarks>
///     <para>
///         Trace context parsing is left to the platform's <c>DistributedContextPropagator</c> — its version
///         prefixes, flag bits and <c>tracestate</c> list rules are subtle and not worth duplicating. Baggage
///         is different: the platform offers no way to serialize a standalone dictionary, only to inject the
///         baggage already attached to an <c>Activity</c>. Synapse keeps baggage on the context rather than on
///         the activity, so it owns both directions here — and owning both is what guarantees they agree about
///         percent-encoding.
///     </para>
/// </remarks>
internal static class BaggageCodec
{
    /// <summary>
    ///     Formats entries as a <c>baggage</c> header value, percent-encoding values per the specification.
    /// </summary>
    /// <returns>The header value, or <c>null</c> when there is nothing to write.</returns>
    public static string? Format(IEnumerable<KeyValuePair<string, string>> entries)
    {
        StringBuilder? builder = null;

        foreach (var entry in entries)
        {
            if (!BaggageLimits.IsValidKey(entry.Key) ||
                !BaggageLimits.IsValidValue(entry.Value))
            {
                continue;
            }

            builder ??= new StringBuilder();
            if (builder.Length > 0)
            {
                builder.Append(',');
            }

            builder.Append(entry.Key)
                .Append('=')
                .Append(Uri.EscapeDataString(entry.Value));
        }

        return builder?.ToString();
    }

    /// <summary>
    ///     Parses a <c>baggage</c> header value.
    /// </summary>
    /// <param name="headerValue">The raw header value, which may be <c>null</c> or malformed.</param>
    /// <param name="dropped">The number of entries rejected as invalid or over the size limits.</param>
    /// <returns>The accepted entries, capped to <see cref="BaggageLimits" />.</returns>
    /// <remarks>
    ///     Malformed or oversized inbound entries are skipped rather than raised: a peer sending bad baggage
    ///     must not be able to fail the receiving request.
    /// </remarks>
    public static Dictionary<string, string> Parse(string? headerValue,
        out int dropped)
    {
        dropped = 0;
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(headerValue))
        {
            return entries;
        }

        var totalBytes = 0;

        foreach (var segment in headerValue.Split(','))
        {
            var separator = segment.IndexOf('=');
            if (separator <= 0)
            {
                dropped++;
                continue;
            }

            var key = segment[..separator].Trim();

            // Any ";" suffix is a baggage property (metadata about the entry); Synapse carries values only.
            var rawValue = segment[(separator + 1)..];
            var propertyStart = rawValue.IndexOf(';');
            if (propertyStart >= 0)
            {
                rawValue = rawValue[..propertyStart];
            }

            var value = Unescape(rawValue.Trim());

            if (!BaggageLimits.IsValidKey(key) ||
                !BaggageLimits.IsValidValue(value))
            {
                dropped++;
                continue;
            }

            // A repeated key overwrites rather than adds, so it neither counts against the entry cap nor adds its
            // predecessor's bytes on top of its own. Counting both made a header that repeats one key exhaust the
            // byte budget early and drop the valid entries that followed (see known issue 039).
            var entryBytes = BaggageLimits.MeasureEntry(key, value);
            var replacedBytes = entries.TryGetValue(key, out var existing)
                ? BaggageLimits.MeasureEntry(key, existing)
                : 0;

            if (replacedBytes == 0 &&
                entries.Count >= BaggageLimits.MaxEntryCount)
            {
                dropped++;
                continue;
            }

            var projectedBytes = totalBytes - replacedBytes + entryBytes;
            if (projectedBytes > BaggageLimits.MaxTotalBytes)
            {
                dropped++;
                continue;
            }

            entries[key] = value;
            totalBytes = projectedBytes;
        }

        return entries;
    }

    private static string Unescape(string value)
    {
        if (!value.Contains('%'))
        {
            return value;
        }

        try
        {
            return Uri.UnescapeDataString(value);
        }
        catch (UriFormatException)
        {
            // A malformed escape sequence is the peer's problem; keep the raw text rather than dropping it.
            return value;
        }
    }
}
