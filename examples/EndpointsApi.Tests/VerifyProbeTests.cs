using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace UnambitiousFx.Examples.EndpointsApi.Tests;

/// <summary>
///     End-to-end coverage of the self-handled tier at the arity with no response body: the same
///     generated binding as its value-returning neighbour, and a <c>204</c> the base class supplies.
/// </summary>
public sealed class VerifyProbeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public VerifyProbeTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // The arity's claim: success is the absence of a failure, so the endpoint returns a non-generic
    // Result and the tier turns it into a 204 with no body of any kind.
    [Fact]
    public async Task VerifyProbe_ForAKnownProbe_Returns204WithNoBody()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.PostAsync("/ops/probes/tasks/verify", content: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(string.Empty,
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    // A failure returned by ExecuteAsync goes through the registered IFailureHttpMapper, so this
    // arity answers an unknown probe exactly as ProbeEndpoint's value arity does.
    [Fact]
    public async Task VerifyProbe_ForAnUnknownProbe_Returns404FromTheRegisteredFailureMapper()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.PostAsync("/ops/probes/nope/verify", content: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // What decides whether a body is read is the binding, not the verb: every property comes off the
    // route, so a POST body is accepted and ignored rather than rejected — the shape ArchiveTaskEndpoint
    // pins one tier up.
    [Fact]
    public async Task VerifyProbe_WithABodyItNeverReads_StillReturns204()
    {
        // Arrange
        var client = _factory.CreateClient();
        var content = new StringContent("""{"ignored":true}""", Encoding.UTF8, "application/json");

        // Act
        var response = await client.PostAsync("/ops/probes/store/verify", content,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // The OpenAPI consequence of that binding: a 204 declared, and no requestBody at all.
    [Fact]
    public async Task GetOpenApi_DocumentsThe204AndNoRequestBody()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var document = await client.GetStringAsync("/openapi/v1.json",
            TestContext.Current.CancellationToken);
        using var parsed = JsonDocument.Parse(document);
        var operation = parsed.RootElement.GetProperty("paths")
            .GetProperty("/ops/probes/{probe}/verify")
            .GetProperty("post");

        // Assert
        Assert.True(operation.GetProperty("responses").TryGetProperty("204", out _),
            "POST /ops/probes/{probe}/verify does not document its 204.");
        Assert.True(operation.GetProperty("responses").TryGetProperty("404", out _),
            "POST /ops/probes/{probe}/verify does not document the 404 Configure declares.");
        Assert.False(operation.TryGetProperty("requestBody", out _),
            "POST /ops/probes/{probe}/verify binds only from the route and should declare no request body.");
    }
}
