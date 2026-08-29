using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace UnambitiousFx.Examples.EndpointsApi.Tests;

/// <summary>
///     Coverage of the success mappers beyond <c>Created()</c>: <c>Accepted()</c>, <c>NoContent()</c>
///     on an endpoint whose handler does return a value, and the general <c>StatusCode(int)</c>.
/// </summary>
public sealed class ResponseMapperTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ResponseMapperTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ArchiveTask_Returns202WithALocation()
    {
        // Arrange
        var client = _factory.CreateClient();
        var id = await CreateTaskAsync(client, "to be archived");

        // Act — an empty body is not enough for a POST; see the endpoint's remarks.
        var response = await client.PostAsJsonAsync($"/tasks/{id}/archive", new { },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal($"/tasks/{id}", response.Headers.Location!.ToString());
    }

    // Pins the consequence of a POST reading a body it has no property to fill from: the caller has
    // to send {} even though TaskId comes off the route. Both 400s are asserted because which one
    // you get depends on whether a content type was sent at all.
    [Fact]
    public async Task ArchiveTask_WithNoBody_Returns400()
    {
        // Arrange
        var client = _factory.CreateClient();
        var id = await CreateTaskAsync(client, "to be archived without a body");

        // Act — no content type, so the JSON content-type check fails first.
        var noContentType = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, $"/tasks/{id}/archive"),
            TestContext.Current.CancellationToken);

        // Act — a declared JSON content type with nothing in the body.
        var emptyBody = new HttpRequestMessage(HttpMethod.Post, $"/tasks/{id}/archive")
        {
            Content = new StringContent("", Encoding.UTF8, "application/json")
        };
        var emptyBodyResponse = await client.SendAsync(emptyBody,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, noContentType.StatusCode);
        Assert.Contains("required to be JSON",
            await noContentType.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.BadRequest, emptyBodyResponse.StatusCode);
        Assert.Contains("empty or null",
            await emptyBodyResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    // NoContent() on an Endpoint<TRequest, TResponse>: the handler produced a TaskDto and the wire
    // contract deliberately drops it. A 200 with a body here would mean the mapper was ignored.
    [Fact]
    public async Task RetitleTask_Returns204AndNoBodyDespiteTheHandlerReturningOne()
    {
        // Arrange
        var client = _factory.CreateClient();
        var id = await CreateTaskAsync(client, "before the retitle");

        // Act
        var response = await client.PutAsJsonAsync($"/tasks/{id}/title",
            new { title = "after the retitle" },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        // Assert — the work still happened; only the response body was withheld.
        var fetched = await client.GetFromJsonAsync<TaskPayload>($"/tasks/{id}",
            TestContext.Current.CancellationToken);
        Assert.Equal("after the retitle", fetched!.Title);
    }

    [Fact]
    public async Task CompactTasks_RespondsWithTheStatusCodeItDeclared()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/tasks/compact", new { },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.ResetContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    // The declared success code has to reach the document too, not just the wire — the gap
    // docs/known-issues/051 was about, now asserted for the two codes only these endpoints produce.
    [Fact]
    public async Task GetOpenApi_DocumentsTheDeclaredSuccessCodes()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var document = await client.GetStringAsync("/openapi/v1.json",
            TestContext.Current.CancellationToken);
        using var parsed = JsonDocument.Parse(document);
        var paths = parsed.RootElement.GetProperty("paths");

        // Assert
        Assert.True(
            paths.GetProperty("/tasks/{taskId}/archive").GetProperty("post")
                .GetProperty("responses").TryGetProperty("202", out _),
            "POST /tasks/{taskId}/archive does not document its 202.");
        Assert.True(
            paths.GetProperty("/tasks/{taskId}/title").GetProperty("put")
                .GetProperty("responses").TryGetProperty("204", out _),
            "PUT /tasks/{taskId}/title does not document its 204.");
        Assert.True(
            paths.GetProperty("/tasks/compact").GetProperty("post")
                .GetProperty("responses").TryGetProperty("205", out _),
            "POST /tasks/compact does not document its 205.");
    }

    private static async Task<Guid> CreateTaskAsync(HttpClient client, string title)
    {
        var response = await client.PostAsJsonAsync("/tasks", new { title },
            TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<TaskCreatedPayload>(
            TestContext.Current.CancellationToken);
        return created!.TaskId;
    }

    private sealed record TaskCreatedPayload(Guid TaskId);

    private sealed record TaskPayload(Guid Id, string Title);
}
