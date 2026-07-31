namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Provides access to the current <see cref="IContext" />.
/// </summary>
public interface IContextAccessor
{
    /// <summary>
    ///     Gets the current context, creating it on first access.
    /// </summary>
    /// <remarks>
    ///     Reading this property is what materializes the context for the scope. Use
    ///     <see cref="IsInitialized" /> when you need to know whether a context exists without bringing one
    ///     into being.
    /// </remarks>
    IContext Context { get; }

    /// <summary>
    ///     Gets a value indicating whether a context has already been created for the current scope.
    /// </summary>
    /// <remarks>
    ///     Lets a caller distinguish "no work happened in this scope" from "work happened" without the
    ///     side effect of creating a context. Reading <see cref="Context" /> to find out would always
    ///     report <c>true</c>.
    /// </remarks>
    bool IsInitialized { get; }
}
