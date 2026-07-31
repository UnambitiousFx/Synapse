using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Contexts;

/// <summary>
///     Baggage storage that enforces the W3C size limits eagerly, so an entry is never accepted that a
///     downstream intermediary would silently truncate.
/// </summary>
/// <remarks>
///     <para>
///         Safe for concurrent use. One <see cref="Context" /> is shared by every consumer in a scope, and an
///         event published to several handlers runs them concurrently against that one instance, so two handlers
///         setting baggage — or one setting it while the propagator or the log-enrichment behavior enumerates it
///         — are ordinary occurrences rather than misuse (see known issue 035).
///     </para>
///     <para>
///         The published dictionary is never mutated: a writer copies it, changes the copy, and swaps the
///         reference in. That keeps readers lock-free and gives an enumeration a stable snapshot rather than a
///         view that shifts underneath it, at the cost of a small copy per write — baggage is written a handful
///         of times per unit of work and read on every boundary crossing. A lock still serializes writers,
///         because the size check spans the dictionary and the byte counter.
///     </para>
/// </remarks>
internal sealed class BaggageCollection
{
    private static readonly Dictionary<string, string> NoEntries = new(0, StringComparer.Ordinal);

    private readonly object _gate = new();
    private Dictionary<string, string> _entries = NoEntries;
    private int _totalBytes;

    public IReadOnlyDictionary<string, string> Entries => Volatile.Read(ref _entries);

    public bool Set(string key,
        string value)
    {
        if (!BaggageLimits.IsValidKey(key) ||
            !BaggageLimits.IsValidValue(value))
        {
            return false;
        }

        lock (_gate)
        {
            var current = _entries;
            var addedBytes = BaggageLimits.MeasureEntry(key, value);
            var replacedBytes = current.TryGetValue(key, out var existing)
                ? BaggageLimits.MeasureEntry(key, existing)
                : 0;

            if (replacedBytes == 0 &&
                current.Count >= BaggageLimits.MaxEntryCount)
            {
                return false;
            }

            var projectedBytes = _totalBytes - replacedBytes + addedBytes;
            if (projectedBytes > BaggageLimits.MaxTotalBytes)
            {
                return false;
            }

            var next = new Dictionary<string, string>(current, StringComparer.Ordinal)
            {
                [key] = value
            };

            _totalBytes = projectedBytes;
            Volatile.Write(ref _entries, next);
            return true;
        }
    }

    /// <summary>
    ///     Applies several entries in one write, skipping any the limits refuse.
    /// </summary>
    /// <remarks>
    ///     Copy-on-write costs a copy per published change, so restoring inbound baggage entry by entry would copy
    ///     the dictionary once per entry. Boundary restore is the one place where entries arrive as a batch, so it
    ///     gets a batch write. The per-entry rules are the same as <see cref="Set" />: an entry that fails
    ///     validation or does not fit is skipped, and the ones around it still land.
    /// </remarks>
    public void SetRange(IReadOnlyDictionary<string, string> entries)
    {
        lock (_gate)
        {
            var current = _entries;
            Dictionary<string, string>? next = null;
            var totalBytes = _totalBytes;

            foreach (var entry in entries)
            {
                if (!BaggageLimits.IsValidKey(entry.Key) ||
                    !BaggageLimits.IsValidValue(entry.Value))
                {
                    continue;
                }

                var source = next ?? current;
                var addedBytes = BaggageLimits.MeasureEntry(entry.Key, entry.Value);
                var replacedBytes = source.TryGetValue(entry.Key, out var existing)
                    ? BaggageLimits.MeasureEntry(entry.Key, existing)
                    : 0;

                if (replacedBytes == 0 &&
                    source.Count >= BaggageLimits.MaxEntryCount)
                {
                    continue;
                }

                var projectedBytes = totalBytes - replacedBytes + addedBytes;
                if (projectedBytes > BaggageLimits.MaxTotalBytes)
                {
                    continue;
                }

                next ??= new Dictionary<string, string>(current, StringComparer.Ordinal);
                next[entry.Key] = entry.Value;
                totalBytes = projectedBytes;
            }

            if (next is null)
            {
                return;
            }

            _totalBytes = totalBytes;
            Volatile.Write(ref _entries, next);
        }
    }

    public bool Remove(string key)
    {
        lock (_gate)
        {
            var current = _entries;
            if (!current.TryGetValue(key, out var value))
            {
                return false;
            }

            var next = new Dictionary<string, string>(current, StringComparer.Ordinal);
            next.Remove(key);

            _totalBytes -= BaggageLimits.MeasureEntry(key, value);
            Volatile.Write(ref _entries, next);
            return true;
        }
    }

    public bool TryGet(string key,
        out string? value)
    {
        return Volatile.Read(ref _entries)
                       .TryGetValue(key, out value);
    }
}
