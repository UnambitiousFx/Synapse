using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace UnambitiousFx.Examples.EndpointsApi.Tests;

/// <summary>
///     End-to-end coverage of <c>MappedEndpoint&lt;THttpRequest, TRequest, TResponse, THttpResponse&gt;</c>,
///     the high-level base class whose wire contract is deliberately not its message.
/// </summary>
public sealed class MappedEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public MappedEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // Pins the whole point of the tier: the caller speaks the v1 contract ("name"), the handler
    // receives the internal message ("Title"), and the caller gets back a shape the message does not
    // have. Both mappings run, or one of these assertions fails.
    [Fact]
    public async Task PostV1Tasks_MapsTheWireContractOntoTheMessageAndBack()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/v1/tasks",
            new { name = "written through the v1 contract" },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<CreateTaskResponseV1Payload>(
            TestContext.Current.CancellationToken);
        Assert.NotEqual(Guid.Empty, created!.Id);
        Assert.Equal($"/tasks/{created.Id}", created.Self);

        // Assert — ToRequest really did put "name" where the message wanted "Title": the task the
        // shared repository now holds is readable through the ordinary /tasks route.
        var fetched = await client.GetFromJsonAsync<TaskPayload>($"/tasks/{created.Id}",
            TestContext.Current.CancellationToken);
        Assert.Equal("written through the v1 contract", fetched!.Title);
    }

    // The wire DTO is not an IRequest, so the binder generated for it is the one piece of this tier
    // that could plausibly have been skipped. A missing "name" has to fail binding, not bind null.
    [Fact]
    public async Task PostV1Tasks_WithoutTheWireField_Returns400()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act — "title" is what the message calls it, and is exactly what v1 does not accept.
        var response = await client.PostAsJsonAsync("/v1/tasks", new { title = "wrong field" },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetOpenApi_DocumentsTheWireTypesRatherThanTheMessage()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var document = await client.GetStringAsync("/openapi/v1.json",
            TestContext.Current.CancellationToken);
        using var parsed = JsonDocument.Parse(document);
        var operation = parsed.RootElement.GetProperty("paths")
            .GetProperty("/v1/tasks")
            .GetProperty("post");

        // Assert — the request body is THttpRequest and the 200 is THttpResponse. The internal
        // CreateTaskCommand/TaskCreated pair must not appear on this operation at all.
        Assert.True(
            operation.GetProperty("requestBody").GetProperty("content")
                .TryGetProperty("application/json", out _),
            "POST /v1/tasks has no application/json request body.");
        Assert.True(
            operation.GetProperty("responses").TryGetProperty("200", out _),
            "POST /v1/tasks does not document its 200.");
    }

    private sealed record CreateTaskResponseV1Payload(Guid Id, string Self);

    private sealed record TaskPayload(Guid Id, string Title);
}
