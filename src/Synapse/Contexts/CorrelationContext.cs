namespace UnambitiousFx.Synapse.Contexts;

/// <summary>
///     Provides ambient access to the current correlation ID using AsyncLocal storage.
/// </summary>
/// <remarks>
///     <para>
///         This class uses <see cref="AsyncLocal{T}" /> to maintain the correlation ID
///         across asynchronous call contexts. Each async flow maintains its own correlation ID,
///         making it safe for concurrent operations.
///     </para>
///     <para>
///         This pattern is similar to ASP.NET Functional's HttpContext.Current and enables
///         singleton services to access scope-specific data without violating DI lifetime rules.
///     </para>
/// </remarks>
public static class CorrelationContext
{
    private static readonly AsyncLocal<Guid> CorrelationId = new();

    /// <summary>
    ///     Gets or sets the current correlation ID for the ambient async context.
    /// </summary>
    /// <value>
    ///     The correlation ID for the current async flow, or <see cref="Guid.Empty" /> if not set.
    /// </value>
    public static Guid CurrentCorrelationId
    {
        get => CorrelationId.Value;
        set => CorrelationId.Value = value;
    }
}