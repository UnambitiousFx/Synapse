namespace UnambitiousFx.Synapse.Endpoints.Testing.Assertions;

/// <summary>
///     Entry point to the assertions over an <see cref="EndpointResponse" />.
/// </summary>
public static class EndpointResponseAssertionExtensions
{
    /// <summary>Begins asserting on a response.</summary>
    /// <param name="response">The response to assert on.</param>
    /// <returns>The assertions.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="response" /> is <see langword="null" />.</exception>
    public static EndpointResponseAssertions ShouldBe(this EndpointResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return new EndpointResponseAssertions(response);
    }
}
