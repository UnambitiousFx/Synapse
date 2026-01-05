namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Represents an attribute used to indicate that a class is a handler for a specific streaming request type.
/// </summary>
/// <typeparam name="TRequest"></typeparam>
/// <typeparam name="TItem"></typeparam>
public sealed class StreamRequestHandlerAttribute<TRequest, TItem> : Attribute
    where TRequest : IStreamRequest<TItem>
    where TItem : notnull
{
}