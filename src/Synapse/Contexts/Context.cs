using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Abstractions.Exceptions;

namespace UnambitiousFx.Synapse.Contexts;

/// <summary>
///     Default <see cref="IContext" /> implementation.
/// </summary>
/// <remarks>
///     <para>
///         A reference type on purpose. Every consumer holds this through the <see cref="IContext" /> interface —
///         the scoped DI registration, the accessor's field, every injected handler — so a struct would be boxed
///         at each of those points and two boxes could disagree about the identity while sharing the same
///         baggage and feature dictionaries.
///     </para>
///     <para>
///         Because one instance is shared that widely, and an event published to several handlers runs them
///         concurrently, both mutable stores are safe for concurrent use (see known issue 035). Both are also
///         copy-on-write over a shared empty dictionary, so a unit of work that sets no baggage and no features
///         allocates neither store.
///     </para>
/// </remarks>
internal sealed class Context : IContext
{
    private static readonly Dictionary<Type, IContextFeature> NoFeatures = new(0);

    private readonly BaggageCollection _baggage;
    private Dictionary<Type, IContextFeature> _features = NoFeatures;

    public Context(ContextIdentity identity)
    {
        Identity = identity;
        _baggage = new BaggageCollection();
    }

    /// <summary>
    ///     Gets the durable identity of this unit of work.
    /// </summary>
    public ContextIdentity Identity { get; }

    public string TraceId => Identity.TraceId;

    public string? CausationId => Identity.CausationId;

    public DateTimeOffset OccurredAt => Identity.OccurredAt;

    public IReadOnlyDictionary<string, string> Baggage => _baggage.Entries;

    public bool SetBaggage(string key,
        string value)
    {
        return _baggage.Set(key, value);
    }

    public bool RemoveBaggage(string key)
    {
        return _baggage.Remove(key);
    }

    /// <summary>
    ///     Applies baggage recovered at an inbound boundary in a single write.
    /// </summary>
    /// <remarks>
    ///     Same per-entry rules as <see cref="SetBaggage" />; see <see cref="BaggageCollection.SetRange" /> for why
    ///     the batch exists.
    /// </remarks>
    internal void RestoreBaggage(IReadOnlyDictionary<string, string> baggage)
    {
        _baggage.SetRange(baggage);
    }

    public bool TryGetBaggage(string key,
        out string? value)
    {
        return _baggage.TryGet(key, out value);
    }

    public string? GetBaggage(string key)
    {
        return _baggage.TryGet(key, out var value)
            ? value
            : null;
    }

    public bool TryGetFeature<TFeature>(out TFeature? feature) where TFeature : class, IContextFeature
    {
        feature = GetFeature<TFeature>();
        return feature != null;
    }

    public TFeature? GetFeature<TFeature>() where TFeature : class, IContextFeature
    {
        return Volatile.Read(ref _features)
                       .TryGetValue(typeof(TFeature), out var value)
            ? (TFeature)value
            : null;
    }

    public TFeature MustGetFeature<TFeature>() where TFeature : class, IContextFeature
    {
        var feature = GetFeature<TFeature>();
        return feature ?? throw new MissingContextFeatureException(typeof(TFeature));
    }

    public void SetFeature<TFeature>(TFeature feature) where TFeature : class, IContextFeature
    {
        Mutate(features => new Dictionary<Type, IContextFeature>(features)
        {
            [typeof(TFeature)] = feature
        });
    }

    public void RemoveFeature<TFeature>() where TFeature : class, IContextFeature
    {
        Mutate(features =>
        {
            if (!features.ContainsKey(typeof(TFeature)))
            {
                return null;
            }

            var next = new Dictionary<Type, IContextFeature>(features);
            next.Remove(typeof(TFeature));
            return next;
        });
    }

    /// <summary>
    ///     Replaces the feature store with the result of <paramref name="change" />, retrying if another thread
    ///     published in between.
    /// </summary>
    /// <remarks>
    ///     Copy-on-write with an interlocked swap rather than a lock: features carry no cross-field invariant — the
    ///     size cap that forces <see cref="BaggageCollection" /> to hold a lock has no counterpart here — so the
    ///     compare-and-swap is the whole synchronization. Readers hold a dictionary nobody will mutate, so they
    ///     need no lock and cannot see a half-applied change. Returning <c>null</c> from
    ///     <paramref name="change" /> means "nothing to do".
    /// </remarks>
    private void Mutate(Func<Dictionary<Type, IContextFeature>, Dictionary<Type, IContextFeature>?> change)
    {
        while (true)
        {
            var current = Volatile.Read(ref _features);
            var next = change(current);
            if (next is null)
            {
                return;
            }

            if (ReferenceEquals(Interlocked.CompareExchange(ref _features, next, current), current))
            {
                return;
            }
        }
    }
}
