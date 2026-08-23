using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using UnambitiousFx.Examples.EndpointsApi;
using UnambitiousFx.Examples.EndpointsApi.Features.Tasks;

namespace UnambitiousFx.Examples.EndpointsApi.Tests;

/// <summary>
///     End-to-end tests over the real ASP.NET Core pipeline: HTTP in, generated binder, generated
///     <c>IHttpInvoker</c>, CQRS handler, generated response mapping, HTTP out. Each test pins one
///     distinct claim the endpoints design makes — see the individual test comments.
/// </summary>
public sealed class TaskEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public TaskEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // Pins: the declarative .Created(...) success mapping (Task 13's builder API) — the only
    // endpoint in the example with a request body.
    [Fact]
    public async Task Post_WithValidBody_Returns201WithLocation()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/tasks", new { title = "write docs" }, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.StartsWith("/tasks/", response.Headers.Location!.ToString());
    }

    // Pins: convention binding of a route value ("taskId") into a message property via a
    // generated binder — not just that the request succeeds, but that the correct task comes back.
    [Fact]
    public async Task Get_WithRouteParameter_BindsItAndReturnsTheTask()
    {
        // Arrange
        var client = _factory.CreateClient();
        var created = await client.PostAsJsonAsync("/tasks", new { title = "bind me" }, TestContext.Current.CancellationToken);
        var location = created.Headers.Location!.ToString();
        var expectedId = Guid.Parse(location.Split('/').Last());

        // Act
        var response = await client.GetAsync(location, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var task = await response.Content.ReadFromJsonAsync<TaskDto>(TestContext.Current.CancellationToken);
        Assert.NotNull(task);
        Assert.Equal(expectedId, task.Id);
        Assert.Equal("bind me", task.Title);
    }

    // Pins: the void-arity IHttpInvoker overload added in Task 8 after the original design (which
    // assumed every handler returns a value) turned out not to work.
    [Fact]
    public async Task Put_WithNoResponse_Returns204()
    {
        // Arrange
        var client = _factory.CreateClient();
        var created = await client.PostAsJsonAsync("/tasks", new { title = "update me" }, TestContext.Current.CancellationToken);
        var location = created.Headers.Location!.ToString();

        // Act
        var response = await client.PutAsJsonAsync(location, new { title = "updated" }, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // Pins: failures still flow through the existing IFailureHttpMapper untouched — the dividend
    // of keeping the endpoint a thin adapter rather than special-casing error mapping per endpoint.
    // GetTaskQueryHandler returns Result.FailNotFound(...), a typed NotFoundFailure; the default
    // IFailureHttpMapper (UnambitiousFx.Functional.AspNetCore's DefaultFailureHttpMapper) maps that
    // to 404 with a problem+json body — verified empirically by curling the running example
    // (see task-21-report.md). Both halves of the claim — the status *and* the content type — are
    // pinned here; a version that only checked "not success" would pass identically for the 500 a
    // bare string Result.Failure(...) used to produce, which is exactly the gap that was found and
    // fixed in Handlers.cs.
    [Fact]
    public async Task Get_ForUnknownId_Returns404ProblemDetails()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync($"/tasks/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("application/problem+json", response.Content.Headers.ContentType!.ToString());
    }

    // Pins: ASP.NET route-constraint behaviour, not endpoint logic. "not-a-guid" fails the
    // ":guid" route constraint declared on [Get("/{taskId:guid}")], so routing itself rejects the
    // request with 404 before the endpoint (and its own validation) ever runs. This is correct
    // ASP.NET behaviour, not a library bug — worth pinning so nobody "fixes" it into a 400 later.
    [Fact]
    public async Task Get_WithMalformedRouteParameter_Returns404()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/tasks/not-a-guid", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // Pins: Accept-based content negotiation picks the JSON-array transport when the client does
    // not ask for text/event-stream.
    [Fact]
    public async Task GetStream_WithJsonAccept_ReturnsAJsonArray()
    {
        // Arrange
        var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/tasks", new { title = "stream me" }, TestContext.Current.CancellationToken);

        // Act
        var body = await client.GetStringAsync("/tasks/stream", TestContext.Current.CancellationToken);

        // Assert
        Assert.StartsWith("[", body);
        Assert.EndsWith("]", body);
    }

    // Pins: Accept-based content negotiation picks the Server-Sent-Events transport when the
    // client asks for text/event-stream.
    [Fact]
    public async Task GetStream_WithEventStreamAccept_ReturnsServerSentEvents()
    {
        // Arrange
        var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/tasks", new { title = "sse me" }, TestContext.Current.CancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/tasks/stream");
        request.Headers.Add("Accept", "text/event-stream");

        // Act
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("data: ", body);
    }

    // Pins the empty-stream edge case deferred from Task 10: the JSON-array writer opens with "["
    // and closes with "]" unconditionally, so a stream that yields nothing must still produce the
    // literal two-character body "[]" — not an empty body, and not "[,]" from a comma-counting bug.
    // Uses a private, freshly-created factory (rather than the shared fixture) so the in-memory
    // TaskRepository is guaranteed empty regardless of what other tests in this class have created.
    [Fact]
    public async Task GetStream_WithNoItems_ReturnsAnEmptyJsonArray()
    {
        // Arrange
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        // Act
        var body = await client.GetStringAsync("/tasks/stream", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("[]", body);
    }

    // Pins the multi-item edge case deferred from Task 10: every prior streaming test used the
    // same two-item sequence, which cannot distinguish a correct "comma before every item except
    // the first" implementation from a naive "skip the last comma" one — the two only disagree
    // once there are three or more items. A fresh factory keeps the count exact.
    [Fact]
    public async Task GetStream_WithThreeItems_ReturnsAllItemsAsAValidJsonArray()
    {
        // Arrange
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync("/tasks", new { title = "first" }, TestContext.Current.CancellationToken);
        await client.PostAsJsonAsync("/tasks", new { title = "second" }, TestContext.Current.CancellationToken);
        await client.PostAsJsonAsync("/tasks", new { title = "third" }, TestContext.Current.CancellationToken);

        // Act
        var response = await client.GetAsync("/tasks/stream", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var tasks = await response.Content.ReadFromJsonAsync<List<TaskDto>>(TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain(",,", body);
        Assert.DoesNotContain(",]", body);
        Assert.NotNull(tasks);
        Assert.Equal(3, tasks.Count);
        Assert.Equal(["first", "second", "third"], tasks.Select(t => t.Title).OrderBy(t => t));
    }

    // Pins: the explicit metadata from Task 13 lands in the OpenAPI document, and the endpoints
    // are visible there at all — which depends on the MethodInfo the (Delegate) cast in Task 2
    // exists to obtain. The emitted path omits the ":guid" route constraint (ASP.NET strips route
    // constraints from the OpenAPI path key), so it reads "/tasks/{taskId}", matching the source
    // route "/{taskId:guid}" with the constraint removed.
    //
    // Parses the document rather than substring-matching, because a bare Contains("\"/tasks\"")
    // would pass even if minimal-API path registration produced the path with no schema metadata
    // at all — path registration is driven by the route pattern, not by the (Delegate) cast this
    // test exists to guard. Proven to discriminate: temporarily gutting the Accepts/WithMetadata
    // calls in Endpoint<TRequest,TResponse>.CreateDescriptor's ApplyMetadata lambda
    // (src/Synapse.Endpoints/Endpoint.Generic.cs) turns this test red (no requestBody/no schema)
    // while a bare substring check on the path keys would have stayed green throughout — see
    // task-21-report.md for both captured runs.
    [Fact]
    public async Task GetOpenApi_ReturnsADocumentContainingTheTaskPaths()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var document = await client.GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        using var parsed = JsonDocument.Parse(document);
        var paths = parsed.RootElement.GetProperty("paths");

        // Assert — "/tasks" exposes both verbs the example registers on it.
        var tasksPath = paths.GetProperty("/tasks");
        Assert.True(tasksPath.TryGetProperty("get", out _), "\"/tasks\" has no GET operation.");
        Assert.True(tasksPath.TryGetProperty("post", out var postOperation), "\"/tasks\" has no POST operation.");

        // Assert — the POST body's request schema landed (Task 2's Accepts<TRequest> call).
        Assert.True(
            postOperation.GetProperty("requestBody").GetProperty("content").TryGetProperty("application/json", out var requestMediaType),
            "POST /tasks has no application/json request body.");
        Assert.True(requestMediaType.TryGetProperty("schema", out _), "POST /tasks's request body has no schema.");

        // Assert — "/tasks/{taskId}" has a GET with a documented response (Task 2's WithMetadata call).
        var taskByIdPath = paths.GetProperty("/tasks/{taskId}");
        var responses = taskByIdPath.GetProperty("get").GetProperty("responses");
        Assert.True(responses.EnumerateObject().Any(), "GET /tasks/{taskId} has no documented responses.");
    }
}
