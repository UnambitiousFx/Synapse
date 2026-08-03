namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Represents a context with properties and methods for managing data during a process or operation.
/// </summary>
public interface IContext
{
    /// <summary>
    ///     Gets the 32-character lowercase hex W3C trace id of the flow this context belongs to.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Constant for the whole flow, so every request, message and retry stemming from the same
    ///         originating action shares it. Never null or empty.
    ///     </para>
    ///     <para>
    ///         This is the trace id, not a separate correlation scheme: the value is byte for byte what a
    ///         tracing backend displays, so one search string works in both application logs and Jaeger, Tempo
    ///         or Application Insights. Whenever an <see cref="System.Diagnostics.Activity" /> exists this
    ///         equals <c>Activity.Current.TraceId.ToHexString()</c>, because that is where it came from; when
    ///         no tracing is wired one is minted so correlation still works.
    ///     </para>
    /// </remarks>
    string TraceId { get; }

    /// <summary>
    ///     Gets the 16-character lowercase hex span id of the immediate predecessor, or <c>null</c> at the root
    ///     of a flow.
    /// </summary>
    /// <remarks>
    ///     Where <see cref="TraceId" /> yields a flat group of everything in a flow, this yields the causality
    ///     tree — which specific caller caused this unit of work. It comes from the inbound
    ///     <c>traceparent</c>, so it is <c>null</c> when nothing upstream was recording: causation is a
    ///     diagnostic nicety, whereas <see cref="TraceId" /> is always present.
    /// </remarks>
    string? CausationId { get; }

    /// <summary>
    ///     Gets the instant this unit of work entered the system.
    /// </summary>
    DateTimeOffset OccurredAt { get; }

    /// <summary>
    ///     Gets the baggage carried by this context.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Baggage is the only free-form state that <b>crosses process boundaries</b>. It is propagated as
    ///         the W3C <c>baggage</c> header, so it is string-keyed, string-valued, and size-limited by
    ///         <see cref="BaggageLimits" />.
    ///     </para>
    ///     <para>
    ///         Because it travels to every downstream service, including third parties outside your control,
    ///         never place confidential values in it. For state that must stay in this process, use a
    ///         <see cref="IContextFeature" /> instead — features are never serialized.
    ///     </para>
    /// </remarks>
    IReadOnlyDictionary<string, string> Baggage { get; }

    /// <summary>
    ///     Adds or updates a baggage entry.
    /// </summary>
    /// <param name="key">The baggage key. Must satisfy <see cref="BaggageLimits.IsValidKey" />.</param>
    /// <param name="value">The baggage value. Must satisfy <see cref="BaggageLimits.IsValidValue" />.</param>
    /// <returns>
    ///     <c>true</c> when the entry was stored; <c>false</c> when it was rejected because the key or value is
    ///     not serializable, or because storing it would exceed <see cref="BaggageLimits.MaxEntryCount" /> or
    ///     <see cref="BaggageLimits.MaxTotalBytes" />.
    /// </returns>
    /// <remarks>
    ///     Rejection is reported rather than thrown so that oversized inbound baggage degrades instead of
    ///     failing the request. Callers that care should log the dropped entry.
    /// </remarks>
    bool SetBaggage(string key,
        string value);

    /// <summary>
    ///     Removes a baggage entry.
    /// </summary>
    /// <param name="key">The baggage key.</param>
    /// <returns><c>true</c> when an entry was removed; otherwise <c>false</c>.</returns>
    bool RemoveBaggage(string key);

    /// <summary>
    ///     Tries to get a baggage value.
    /// </summary>
    /// <param name="key">The baggage key.</param>
    /// <param name="value">When this method returns, the value if found; otherwise <c>null</c>.</param>
    /// <returns><c>true</c> when the entry was found; otherwise <c>false</c>.</returns>
    bool TryGetBaggage(string key,
        out string? value);

    /// <summary>
    ///     Gets a baggage value.
    /// </summary>
    /// <param name="key">The baggage key.</param>
    /// <returns>The value if found; otherwise <c>null</c>.</returns>
    string? GetBaggage(string key);

    /// <summary>
    ///     Tries to get a feature of the specified type from the context.
    /// </summary>
    /// <typeparam name="TFeature">The type of the feature to retrieve.</typeparam>
    /// <param name="feature">When this method returns, contains the feature if found; otherwise, null.</param>
    /// <returns>True if the feature was found; otherwise, false.</returns>
    bool TryGetFeature<TFeature>(out TFeature? feature)
        where TFeature : class, IContextFeature;

    /// <summary>
    ///     Gets a feature of the specified type from the context.
    /// </summary>
    /// <typeparam name="TFeature">The type of the feature to retrieve.</typeparam>
    /// <returns>The feature if found; otherwise, null.</returns>
    TFeature? GetFeature<TFeature>()
        where TFeature : class, IContextFeature;

    /// <summary>
    ///     Gets a feature of the specified type from the context, throwing an exception if not found.
    /// </summary>
    /// <typeparam name="TFeature">The type of the feature to retrieve.</typeparam>
    /// <returns>The feature.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the feature is not found.</exception>
    TFeature MustGetFeature<TFeature>()
        where TFeature : class, IContextFeature;

    /// <summary>
    ///     Adds or updates a feature in the context.
    /// </summary>
    /// <typeparam name="TFeature">The type of the feature.</typeparam>
    /// <param name="feature">The feature instance to set.</param>
    void SetFeature<TFeature>(TFeature feature)
        where TFeature : class, IContextFeature;

    /// <summary>
    ///     Removes a specific feature of the specified type from the context.
    /// </summary>
    /// <typeparam name="TFeature">The type of the feature to remove.</typeparam>
    void RemoveFeature<TFeature>()
        where TFeature : class, IContextFeature;
}