using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using UnambitiousFx.Examples.EndpointsApi.Features.Tasks;

namespace UnambitiousFx.Examples.EndpointsApi.Tests;

/// <summary>
///     Coverage of the binding rules that the original example did not exercise: a header source
///     (rule 2), an excluded property (rule 1), and a verb with no attribute of its own.
/// </summary>
public sealed class BindingRulesTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public BindingRulesTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // Rule 2: headers are never bound by convention, so this passes only if [FromHeader("X-Actor")]
    // is honoured — and rule 3 and rule 5 have to keep working alongside it on the same message.
    [Fact]
    public async Task PatchTask_BindsTheRouteTheBodyAndTheHeaderTogether()
    {
        // Arrange
        var client = _factory.CreateClient();
        var id = await CreateTaskAsync(client, "before the patch");

        var request = new HttpRequestMessage(HttpMethod.Patch, $"/tasks/{id}")
        {
            Content = JsonContent.Create(new { title = "after the patch" })
        };
        request.Headers.TryAddWithoutValidation("X-Actor", "the-operator");

        // Act
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var patched = await response.Content.ReadFromJsonAsync<TaskPatchedPayload>(
            TestContext.Current.CancellationToken);
        Assert.Equal(id, patched!.TaskId);              // rule 3, from the route
        Assert.Equal("the-operator", patched.Actor);    // rule 2, from the header
        Assert.NotEqual(default, patched.StampedAt);    // rule 1, from the pipeline

        var fetched = await client.GetFromJsonAsync<TaskPayload>($"/tasks/{id}",
            TestContext.Current.CancellationToken);
        Assert.Equal("after the patch", fetched!.Title); // rule 5, from the body
    }

    // An absent header binds null rather than failing: the property is nullable, so it is optional.
    [Fact]
    public async Task PatchTask_WithoutTheHeader_BindsNullRatherThanFailing()
    {
        // Arrange
        var client = _factory.CreateClient();
        var id = await CreateTaskAsync(client, "unattributed");

        var request = new HttpRequestMessage(HttpMethod.Patch, $"/tasks/{id}")
        {
            Content = JsonContent.Create(new { title = "still unattributed" })
        };

        // Act
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var patched = await response.Content.ReadFromJsonAsync<TaskPatchedPayload>(
            TestContext.Current.CancellationToken);
        Assert.Null(patched!.Actor);
        Assert.NotEqual(default, patched.StampedAt);
    }

    // Rule 1's stated promise, quoted from the messages guide: a caller "cannot supply them by
    // guessing the property name". The value sent here is one the pipeline would never produce, so
    // if it survives to the response the exclusion did not hold.
    [Fact]
    public async Task PatchTask_WhenTheCallerGuessesTheNotBoundProperty_IgnoresWhatTheySent()
    {
        // Arrange
        var client = _factory.CreateClient();
        var id = await CreateTaskAsync(client, "guessing game");
        var forged = DateTimeOffset.Parse("2000-01-01T00:00:00Z");

        var request = new HttpRequestMessage(HttpMethod.Patch, $"/tasks/{id}")
        {
            Content = new StringContent(
                $$"""{"title":"guessed","stampedAt":"{{forged:O}}"}""",
                Encoding.UTF8)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        // Act
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var patched = await response.Content.ReadFromJsonAsync<TaskPatchedPayload>(
            TestContext.Current.CancellationToken);
        Assert.NotEqual(forged, patched!.StampedAt);
    }

    // Isolates the layer the end-to-end test above cannot see. StampPatchBehavior overwrites
    // StampedAt unconditionally, so a forged value never survives to the response either way — but
    // [NotBound] on its own would not have stopped it arriving in the message, because a body-carrying
    // verb is bound by deserializing the whole message and System.Text.Json does not read [NotBound].
    // [JsonIgnore] is what makes the exclusion hold at that layer, and this asserts it directly.
    [Fact]
    public void PatchTaskCommand_IsNotDeserializedFromACallerSuppliedStampedAt()
    {
        // Arrange
        const string forged = """{"title":"guessed","stampedAt":"2000-01-01T00:00:00Z"}""";

        // Act
        var command = JsonSerializer.Deserialize<PatchTaskCommand>(forged,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        // Assert
        Assert.Equal("guessed", command.Title);
        Assert.Equal(default, command.StampedAt);
    }

    // [HttpEndpoint("HEAD", …)] is the general form behind [Get]/[Post]/…; HEAD has no attribute of
    // its own, and this is the only route in the example registered through the base attribute.
    //
    // Asserts routing and the negotiated content type, not an empty body: suppressing the body of a
    // HEAD response is the server's job at the protocol layer, and TestServer does not do it while
    // Kestrel does. Verified by hand against the running app — `curl -X HEAD` returns headers with no
    // body and no Transfer-Encoding, where the same route under WebApplicationFactory hands back the
    // full JSON array.
    [Fact]
    public async Task HeadTasks_IsRoutedAndAnswersWithoutABody()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/tasks"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
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

    private sealed record TaskPatchedPayload(Guid TaskId, string? Actor, DateTimeOffset StampedAt);

    private sealed record TaskPayload(Guid Id, string Title);
}
