namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Defines a builder for creating an instance of <see cref="IContext" />.
///     Provides methods to configure various properties of the context before it is built.
/// </summary>
public interface IContextBuilder
{
    /// <summary>
    ///     Sets the correlation ID for the current context being built.
    /// </summary>
    /// <param name="correlationId">The unique identifier to associate with the context for tracking purposes.</param>
    /// <returns>An instance of <see cref="IContextBuilder" /> to allow method chaining.</returns>
    IContextBuilder WithCorrelationId(Guid correlationId);

    /// <summary>
    ///     Adds a set of metadata key-value pairs to the context.
    ///     If a key already exists, its value will be overwritten.
    /// </summary>
    /// <param name="metadata">A dictionary containing metadata key-value pairs to add to the context.</param>
    /// <returns>A reference to the current <see cref="IContextBuilder" /> instance with the updated metadata.</returns>
    IContextBuilder WithMetadata(Dictionary<string, object> metadata);

    /// <summary>
    ///     Adds or updates a metadata entry with the specified key and value.
    /// </summary>
    /// <param name="key">The key of the metadata entry to add or update.</param>
    /// <param name="value">The value of the metadata entry to add or update.</param>
    /// <returns>The updated instance of <see cref="IContextBuilder" /> to allow for method chaining.</returns>
    IContextBuilder WithMetadata(string key, object value);

    /// <summary>
    ///     Creates and returns a new instance of an object implementing the <see cref="IContext" /> interface,
    ///     using the collected configuration data such as correlation ID and metadata.
    /// </summary>
    /// <returns>An instance of <see cref="IContext" /> initialized with the specified properties.</returns>
    IContext Build();
}