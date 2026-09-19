namespace UnambitiousFx.Synapse.Endpoints.Testing.Assertions;

/// <summary>
///     Thrown when an assertion on an <see cref="EndpointResponse" /> does not hold.
/// </summary>
/// <remarks>
///     A plain exception rather than a test framework's own assertion type, so this package works
///     under xunit, NUnit and MSTest without depending on any of them: every runner fails a test that
///     throws.
/// </remarks>
public sealed class EndpointAssertionException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="EndpointAssertionException" /> class.</summary>
    /// <param name="message">The failure message.</param>
    public EndpointAssertionException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="EndpointAssertionException" /> class.</summary>
    /// <param name="message">The failure message.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public EndpointAssertionException(string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}
