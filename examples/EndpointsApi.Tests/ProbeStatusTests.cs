using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace UnambitiousFx.Examples.EndpointsApi.Tests;

/// <summary>
///     End-to-end coverage of the self-handled tier through the real ASP.NET Core pipeline: a request
///     that is not a message, bound by the generated binder, answered by the endpoint itself.
/// </summary>
public sealed class ProbeStatusTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ProbeStatusTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // The claim the tier exists to make: ProbeQuery carries no IRequest<T>, has no handler and no
    // registration, and its Probe property still binds from the route through a generated binder.
    [Fact]
    public async Task GetProbe_BindsTheRouteValueOntoARequestThatIsNotAMessage()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/ops/probes/store", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ProbePayload>(TestContext.Current.CancellationToken);
        Assert.NotNull(payload);
        Assert.Equal("store", payload!.Probe);
        Assert.True(payload.Healthy);
    }

    // A failure returned by ExecuteAsync goes through the registered IFailureHttpMapper, the same one
    // that maps a handler's failure on the dispatching tiers — so the two tiers answer alike.
    [Fact]
    public async Task GetProbe_ForAnUnknownProbe_Returns404FromTheRegisteredFailureMapper()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/ops/probes/nope", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record ProbePayload(string Probe, bool Healthy);
}
