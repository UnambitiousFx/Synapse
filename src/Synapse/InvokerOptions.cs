namespace UnambitiousFx.Synapse;

/// <summary>
///     Configuration options for the <see cref="Invoker" />.
/// </summary>
internal sealed class InvokerOptions
{
    /// <summary>
    ///     Dispatch delegates for request types with a typed response.
    ///     Keyed by the concrete request type (<c>typeof(TRequest)</c>).
    ///     Each delegate is a
    ///     <c>
    ///         Func&lt;IRequest&lt;TResponse&gt;, IDependencyResolver, CancellationToken, ValueTask&lt;Result&lt;TResponse&gt;
    ///         &gt;&gt;
    ///     </c>
    ///     captured at registration time, ensuring NativeAOT compatibility with no reflection or
    ///     <c>MakeGenericType</c> at dispatch time.
    /// </summary>
    public Dictionary<Type, Delegate> RequestDispatchers { get; set; } = new();

    /// <summary>
    ///     Dispatch delegates for streaming request types.
    ///     Keyed by the concrete request type (<c>typeof(TRequest)</c>).
    ///     Each delegate is a
    ///     <c>
    ///         Func&lt;IStreamRequest&lt;TItem&gt;, IDependencyResolver, CancellationToken, IAsyncEnumerable&lt;Result&lt;TItem
    ///         &gt;&gt;&gt;
    ///     </c>
    ///     captured at registration time.
    /// </summary>
    public Dictionary<Type, Delegate> StreamRequestDispatchers { get; set; } = new();
}