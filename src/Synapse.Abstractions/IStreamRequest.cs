namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Represents a streaming request that yields multiple items asynchronously.
///     Use this for queries that return large datasets that should be processed incrementally.
/// </summary>
/// <typeparam name="TResponse">The type of items in the stream. Must be a non-nullable type.</typeparam>
public interface IStreamRequest<out TResponse>
    where TResponse : notnull
{
}