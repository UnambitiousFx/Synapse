using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Contexts;

internal static class ContextBaggage
{
    /// <summary>
    ///     Copies extracted baggage onto a freshly created context.
    /// </summary>
    /// <remarks>
    ///     Entries the context refuses are skipped rather than raised. Validation and the report of what was
    ///     dropped belong to the boundary adapter that read the wire — it is the only layer that knows which
    ///     transport and which peer produced an oversized value, and the only one that can log it usefully.
    /// </remarks>
    public static void Restore(IContext context,
        IReadOnlyDictionary<string, string>? baggage)
    {
        if (baggage is null or { Count: 0 })
        {
            return;
        }

        // The batch write exists because the store is copy-on-write; a custom IContext implementation has no
        // such contract to honour, so it gets the entry-by-entry path.
        if (context is Context concrete)
        {
            concrete.RestoreBaggage(baggage);
            return;
        }

        foreach (var entry in baggage)
        {
            context.SetBaggage(entry.Key, entry.Value);
        }
    }
}
