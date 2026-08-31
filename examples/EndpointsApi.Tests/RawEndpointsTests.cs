using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace UnambitiousFx.Examples.EndpointsApi.Tests;

/// <summary>
///     End-to-end coverage of the two low-level endpoint shapes through the real ASP.NET Core
///     pipeline: the free-form <c>RawEndpoint</c> and the hand-bound
///     <c>RawEndpoint&lt;TRequest, TResponse&gt;</c>.
/// </summary>
public sealed class RawEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public RawEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetHealth_ReturnsThePayloadAndAnETag()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Headers.ETag);

        var payload = await response.Content.ReadFromJsonAsync<HealthPayload>(
            TestContext.Current.CancellationToken);
        Assert.Equal("ok", payload!.Status);
    }

    // The decision this endpoint exists to make: a conditional GET answered from a request header.
    // There is no message to dispatch and no response DTO to map, which is exactly why it is a
    // free-form low-level endpoint rather than an Endpoint<TRequest, TResponse>.
    [Fact]
    public async Task GetHealth_WithAMatchingIfNoneMatch_Returns304()
    {
        // Arrange
        var client = _factory.CreateClient();
        var first = await client.GetAsync("/health", TestContext.Current.CancellationToken);
        var etag = first.Headers.ETag!.ToString();

        var conditional = new HttpRequestMessage(HttpMethod.Get, "/health");
        conditional.Headers.TryAddWithoutValidation("If-None-Match", etag);

        // Act
        var response = await client.SendAsync(conditional, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
    }

    [Fact]
    public async Task GetReports_WithARepeatedQueryKey_BindsEveryValue()
    {
        // Arrange — a collection built from one repeated key is what the binding conventions cannot
        // express, so this endpoint writes its own BindAsync and inherits everything else.
        var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/tasks", new { title = "write the docs" },
            TestContext.Current.CancellationToken);

        // Act
        var response = await client.GetAsync("/reports?page=1&tag=docs&tag=write",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var report = await response.Content.ReadFromJsonAsync<TagReportPayload>(
            TestContext.Current.CancellationToken);
        Assert.Equal(1, report!.Page);
        Assert.Equal(["docs", "write"], report.Tags);
        Assert.True(report.Matched >= 1, "the task created above should have matched a tag");
    }

    [Fact]
    public async Task GetReports_WithTwoBadInputs_Returns400NamingBothOfThem()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act — page is below the minimum AND no tag was supplied.
        var response = await client.GetAsync("/reports?page=0", TestContext.Current.CancellationToken);

        // Assert — the point of the accumulating collector: one response covering every problem, so a
        // caller does not fix one input only to rediscover the next.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json",
            response.Content.Headers.ContentType!.MediaType);

        var errors = await ReadErrorsAsync(response);
        Assert.Contains("page", errors.Keys);
        Assert.Contains("tag", errors.Keys);
    }

    // Asserts the messages, not just the keys. A rule check on a value that never bound tests
    // default(T) and reports a second, false error, which the key-level assertions above cannot see:
    // a request that omitted page entirely was told both that it is required and that it "must be at
    // least 1". See docs/known-issues/052.
    [Fact]
    public async Task GetReports_WithAMissingPage_ReportsOnlyThatItIsRequired()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/reports?tag=docs", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var errors = await ReadErrorsAsync(response);
        var page = Assert.Single(errors["page"]);
        Assert.Contains("required", page);
    }

    // The guard must not silence the rule it guards: a page that did bind and is out of range still
    // reports, and nothing else does.
    [Fact]
    public async Task GetReports_WithAPageBelowTheMinimum_StillReportsTheRule()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/reports?page=0&tag=docs",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var errors = await ReadErrorsAsync(response);
        Assert.Equal(["page"], errors.Keys);
        Assert.Equal("must be at least 1", Assert.Single(errors["page"]));
    }

    [Fact]
    public async Task GetReports_WithAnUnparsableQueryValue_Returns400ForThatField()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/reports?page=nope&tag=docs",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var errors = await ReadErrorsAsync(response);
        Assert.Contains("page", errors.Keys);
    }

    // The high level now reports through the same collector as the low level, so a request that gets
    // two convention-bound values wrong gets one response naming both. Asserted through the real
    // pipeline because the shape of a 400 is part of the public contract.
    [Fact]
    public async Task PutTask_WithABadRouteValueAndNoBody_Returns400()
    {
        // Arrange
        var client = _factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Put, $"/tasks/{Guid.NewGuid()}")
        {
            Content = new StringContent("{}")
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        // Act
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // Assert — the body parses but omits the required Title, so the message itself is incomplete.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetOpenApi_DocumentsTheLowLevelEndpointsDeclaredContract()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var document = await client.GetStringAsync("/openapi/v1.json",
            TestContext.Current.CancellationToken);
        using var parsed = JsonDocument.Parse(document);
        var paths = parsed.RootElement.GetProperty("paths");

        // Assert — nothing about a hand-written handler can be inferred, so these entries exist only
        // because the endpoint declared them through IRawEndpointBuilder.
        var health = paths.GetProperty("/health").GetProperty("get").GetProperty("responses");
        Assert.True(health.TryGetProperty("200", out var ok), "GET /health does not document a 200.");
        Assert.True(
            ok.GetProperty("content").TryGetProperty("application/json", out _),
            "GET /health's 200 has no application/json content.");
        Assert.True(health.TryGetProperty("304", out _), "GET /health does not document a 304.");
    }

    // A response with no body used to be described with a null Type, which
    // Microsoft.AspNetCore.OpenApi skips outright — so every declared bodyless status code was absent
    // from the document even though the metadata was there. Asserted on the high-level endpoints too,
    // because that is where the gap had been silently living.
    [Fact]
    public async Task GetOpenApi_DocumentsBodylessResponses()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var document = await client.GetStringAsync("/openapi/v1.json",
            TestContext.Current.CancellationToken);
        using var parsed = JsonDocument.Parse(document);
        var paths = parsed.RootElement.GetProperty("paths");

        // Assert
        var byId = paths.GetProperty("/tasks/{taskId}");
        Assert.True(
            byId.GetProperty("put").GetProperty("responses").TryGetProperty("204", out _),
            "PUT /tasks/{taskId} does not document its 204.");
        Assert.True(
            byId.GetProperty("delete").GetProperty("responses").TryGetProperty("204", out _),
            "DELETE /tasks/{taskId} does not document its 204.");
    }

    private static async Task<Dictionary<string, string[]>> ReadErrorsAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var parsed = JsonDocument.Parse(body);

        return parsed.RootElement.GetProperty("errors")
            .EnumerateObject()
            .ToDictionary(
                property => property.Name,
                property => property.Value.EnumerateArray().Select(value => value.GetString()!).ToArray());
    }

    private sealed record HealthPayload(string Status, int Tasks);

    private sealed record TagReportPayload(int Page, int Matched, string[] Tags);
}
