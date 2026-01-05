using UnambitiousFx.Functional;

namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Represents a context with properties and methods for managing data during a process or operation.
/// </summary>
public interface IContext
{
    /// <summary>
    ///     Gets the unique identifier that represents a correlation ID for tracing or tracking purposes within the context.
    ///     This property is immutable and ensures consistent identification of a specific operation or event flow.
    /// </summary>
    Guid CorrelationId { get; }

    /// <summary>
    ///     Gets a read-only dictionary containing metadata key-value pairs associated with the context.
    /// </summary>
    IReadOnlyDictionary<string, object> Metadata { get; }

    /// <summary>
    ///     Sets a metadata value for the specified key in the context.
    /// </summary>
    /// <param name="key">The metadata key.</param>
    /// <param name="value">The metadata value to set.</param>
    void SetMetadata(string key,
        object value);

    /// <summary>
    ///     Removes a metadata entry for the specified key from the context.
    /// </summary>
    /// <param name="key">The metadata key.</param>
    bool RemoveMetadata(string key);

    /// <summary>
    ///     Tries to get a metadata value for the specified key.
    /// </summary>
    /// <typeparam name="T">The type of the metadata value.</typeparam>
    /// <param name="key">The metadata key.</param>
    /// <param name="value">When this method returns, contains the metadata value if found; otherwise, the default value.</param>
    /// <returns>True if the metadata value was found; otherwise, false.</returns>
    bool TryGetMetadata<T>(string key,
        out T? value);

    /// <summary>
    ///     Gets a metadata value for the specified key.
    /// </summary>
    /// <typeparam name="T">The type of the metadata value.</typeparam>
    /// <param name="key">The metadata key.</param>
    /// <returns>The metadata value if found; otherwise, the default value for the type.</returns>
    T? GetMetadata<T>(string key);

    /// <summary>
    ///     Publishes an event asynchronously.
    /// </summary>
    /// <typeparam name="TEvent">The type of the event to be published, which must implement <see cref="IEvent" />.</typeparam>
    /// <param name="event">The event instance to be published.</param>
    /// <param name="cancellationToken">
    ///     A cancellation token to observe while waiting for the task to complete. Defaults to
    ///     <see cref="CancellationToken.None" /> if not provided.
    /// </param>
    /// <returns>
    ///     A <see cref="ValueTask" /> containing a <see cref="Result" /> that indicates the success or failure of the
    ///     operation.
    /// </returns>
    ValueTask<Result> PublishEventAsync<TEvent>(TEvent @event,
        CancellationToken cancellationToken = default)
        where TEvent : class, IEvent;

    /// <summary>
    ///     Publishes an event asynchronously with the specified publish mode and distribution mode.
    /// </summary>
    /// <typeparam name="TEvent">
    ///     The type of the event to be published, which must implement <see cref="IEvent" />.
    /// </typeparam>
    /// <param name="event">
    ///     The event instance to be published.
    /// </param>
    /// <param name="mode">
    ///     The mode in which the event should be published, specified as a <see cref="PublishMode" />.
    /// </param>
    /// <param name="distributionMode">
    ///     The distribution mode specifying how the event should be distributed, using <see cref="DistributionMode" />.
    /// </param>
    /// <param name="cancellationToken">
    ///     A cancellation token to observe while waiting for the task to complete. Defaults to
    ///     <see cref="CancellationToken.None" /> if not provided.
    /// </param>
    /// <returns>
    ///     A <see cref="ValueTask" /> containing a <see cref="Result" /> that indicates the success or failure of the
    ///     operation.
    /// </returns>
    ValueTask<Result> PublishEventAsync<TEvent>(TEvent @event,
        PublishMode mode,
        DistributionMode distributionMode,
        CancellationToken cancellationToken = default)
        where TEvent : class, IEvent;

    /// <summary>
    ///     Commits all pending events asynchronously within the current scope.
    /// </summary>
    /// <param name="cancellationToken">
    ///     A cancellation token to observe while waiting for the task to complete. Defaults to
    ///     <see cref="CancellationToken.None" /> if not provided.
    /// </param>
    /// <returns>
    ///     A <see cref="ValueTask" /> containing a <see cref="Result" /> that indicates the success or failure of the
    ///     operation.
    /// </returns>
    /// <remarks>
    ///     This method processes all events that were published with <see cref="PublishMode.Outbox" />
    ///     in the current scope. It delegates to <see cref="IOutboxCommit.CommitAsync" />.
    /// </remarks>
    ValueTask<Result> CommitEventsAsync(CancellationToken cancellationToken = default);

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
}