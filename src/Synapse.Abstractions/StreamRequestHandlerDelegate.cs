using UnambitiousFx.Functional;

namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Represents a delegate that handles a streaming request and returns an async enumerable of results.
/// </summary>
/// <typeparam name="TItem">The type of items in the stream. Must be a non-nullable type.</typeparam>
/// <returns>An asynchronous enumerable sequence of results.</returns>
public delegate IAsyncEnumerable<Result<TItem>> StreamRequestHandlerDelegate<TItem>()
    where TItem : notnull;