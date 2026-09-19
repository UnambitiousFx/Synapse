using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace UnambitiousFx.Examples.EndpointsApi.Tests;

/// <summary>
///     Coverage of a stream whose request arrives in a body, and of what a failed item does to a
///     response that has already started.
/// </summary>
public sealed class StreamSearchTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public StreamSearchTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // GET /tasks/stream binds nothing; this one binds Title from the JSON body, which is the part a
    // POST stream could plausibly not have generated a binder for.
    [Fact]
    public async Task PostStreamSearch_BindsTheBodyAndStreamsTheMatches()
    {
        // Arrange
        var client = _factory.CreateClient();
        var marker = $"marker-{Guid.NewGuid():N}";
        await CreateTaskAsync(client, $"{marker} one");
        await CreateTaskAsync(client, $"{marker} two");
        await CreateTaskAsync(client, "unrelated");

        // Act
        var response = await client.PostAsJsonAsync("/tasks/stream/search", new { title = marker },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var titles = await ReadTitlesAsync(response);
        Assert.Equal(2, titles.Count);
        Assert.All(titles, title => Assert.Contains(marker, title));
    }

    [Fact]
    public async Task PostStreamSearch_WithAnEventStreamAccept_NegotiatesServerSentEvents()
    {
        // Arrange
        var client = _factory.CreateClient();
        var marker = $"sse-{Guid.NewGuid():N}";
        await CreateTaskAsync(client, $"{marker} one");

        var request = new HttpRequestMessage(HttpMethod.Post, "/tasks/stream/search")
        {
            Content = JsonContent.Create(new { title = marker })
        };
        request.Headers.TryAddWithoutValidation("Accept", "text/event-stream");

        // Act
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // Assert — the same negotiation the bodyless stream does; only the input differs.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType!.MediaType);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains(marker, body);
    }

    // Pins the contract StreamEndpoint documents: the status line is committed before the first
    // item, so a failure part-way through cannot change it — the item is skipped and the rest of the
    // sequence still arrives.
    [Fact]
    public async Task PostStreamSearch_WithAFailingItem_SkipsItAndKeepsStreaming()
    {
        // Arrange — a blank title is what the handler turns into a failed item.
        var client = _factory.CreateClient();
        var marker = $"partial-{Guid.NewGuid():N}";
        await CreateTaskAsync(client, $"{marker} good");
        await CreateTaskAsync(client, "   ");

        // Act
        var response = await client.PostAsJsonAsync("/tasks/stream/search", new { title = marker },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var titles = await ReadTitlesAsync(response);
        Assert.Equal($"{marker} good", Assert.Single(titles));
    }

    // StreamEndpoint is a body-carrying tier like any other when its verb carries a body, so it has
    // to declare what it accepts: without it the input shape is undiscoverable from the document, and
    // routing cannot reject a wrong content type. See docs/known-issues/065.
    [Fact]
    public async Task GetOpenApi_DocumentsThePostStreamRequestBody()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var document = await client.GetStringAsync("/openapi/v1.json",
            TestContext.Current.CancellationToken);
        using var parsed = JsonDocument.Parse(document);
        var operation = parsed.RootElement.GetProperty("paths")
            .GetProperty("/tasks/stream/search")
            .GetProperty("post");

        // Assert
        Assert.True(
            operation.GetProperty("requestBody").GetProperty("content")
                .TryGetProperty("application/json", out var mediaType),
            "POST /tasks/stream/search has no application/json request body.");
        Assert.True(mediaType.TryGetProperty("schema", out _),
            "POST /tasks/stream/search's request body has no schema.");
        Assert.True(operation.GetProperty("responses").TryGetProperty("200", out _),
            "POST /tasks/stream/search does not document its 200.");
    }

    // The bodyless stream must stay bodyless: declaring Accepts is guarded on the verb, so
    // GET /tasks/stream keeps publishing no request body.
    [Fact]
    public async Task GetOpenApi_LeavesTheBodylessStreamWithoutARequestBody()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var document = await client.GetStringAsync("/openapi/v1.json",
            TestContext.Current.CancellationToken);
        using var parsed = JsonDocument.Parse(document);
        var operation = parsed.RootElement.GetProperty("paths")
            .GetProperty("/tasks/stream")
            .GetProperty("get");

        // Assert
        Assert.False(operation.TryGetProperty("requestBody", out _),
            "GET /tasks/stream should not document a request body.");
    }

    // The consequence on the wire: routing rejects a non-JSON content type before the binder runs,
    // the same way it does for every other body-carrying endpoint.
    [Fact]
    public async Task PostStreamSearch_WithANonJsonContentType_Returns415()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/tasks/stream/search")
        {
            Content = new StringContent("not json", Encoding.UTF8, "text/plain")
        };

        // Act
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    private static async Task<List<string>> ReadTitlesAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var parsed = JsonDocument.Parse(body);

        return parsed.RootElement.EnumerateArray()
            .Select(item => item.GetProperty("title").GetString()!)
            .ToList();
    }

    private static async Task CreateTaskAsync(HttpClient client, string title)
    {
        var response = await client.PostAsJsonAsync("/tasks", new { title },
            TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
