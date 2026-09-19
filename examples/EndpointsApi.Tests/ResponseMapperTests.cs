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

        // Act — no body at all: nothing on this message binds from one.
        var response = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, $"/tasks/{id}/archive"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal($"/tasks/{id}", response.Headers.Location!.ToString());
    }

    // The fix for docs/known-issues/067, from the caller's side: a POST whose every property comes
    // off the route used to answer 400 unless "{}" was sent. Both shapes that used to fail now work,
    // and a caller that does send a body is not punished for it either.
    [Fact]
    public async Task ArchiveTask_WithNoBody_Succeeds()
    {
        // Arrange
        var client = _factory.CreateClient();
        var id = await CreateTaskAsync(client, "to be archived without a body");

        // Act
        var noContentType = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, $"/tasks/{id}/archive"),
            TestContext.Current.CancellationToken);

        var emptyJson = new HttpRequestMessage(HttpMethod.Post, $"/tasks/{id}/archive")
        {
            Content = new StringContent("", Encoding.UTF8, "application/json")
        };
        var emptyJsonResponse = await client.SendAsync(emptyJson,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, noContentType.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, emptyJsonResponse.StatusCode);
    }

    // A body sent to an endpoint that binds nothing from one is ignored rather than rejected. Worth
    // pinning because it is the loosening this change makes: routing no longer has an Accepts
    // declaration to match a content type against, so nothing answers 415 here any more.
    [Fact]
    public async Task ArchiveTask_WithAnUnexpectedBody_IgnoresIt()
    {
        // Arrange
        var client = _factory.CreateClient();
        var id = await CreateTaskAsync(client, "archived despite a stray body");

        var request = new HttpRequestMessage(HttpMethod.Post, $"/tasks/{id}/archive")
        {
            Content = new StringContent("not json at all", Encoding.UTF8, "text/plain")
        };

        // Act
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task CompactTasks_WithNoBody_Succeeds()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, "/tasks/compact"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.ResetContent, response.StatusCode);
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
        var response = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, "/tasks/compact"),
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

    // The document side of docs/known-issues/067: an endpoint that reads no body must not advertise
    // one, while its sibling that does read one still must.
    [Fact]
    public async Task GetOpenApi_DeclaresARequestBodyOnlyWhereOneIsRead()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var document = await client.GetStringAsync("/openapi/v1.json",
            TestContext.Current.CancellationToken);
        using var parsed = JsonDocument.Parse(document);
        var paths = parsed.RootElement.GetProperty("paths");

        // Assert — route-only and propertyless messages declare nothing.
        Assert.False(
            paths.GetProperty("/tasks/{taskId}/archive").GetProperty("post")
                .TryGetProperty("requestBody", out _),
            "POST /tasks/{taskId}/archive declares a request body it never reads.");
        Assert.False(
            paths.GetProperty("/tasks/compact").GetProperty("post")
                .TryGetProperty("requestBody", out _),
            "POST /tasks/compact declares a request body it never reads.");

        // Assert — a body-bound property still declares one.
        Assert.True(
            paths.GetProperty("/tasks/{taskId}/title").GetProperty("put")
                .TryGetProperty("requestBody", out _),
            "PUT /tasks/{taskId}/title should declare a request body.");
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
