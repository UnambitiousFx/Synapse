using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace UnambitiousFx.Examples.EndpointsApi.Tests;

/// <summary>
///     End-to-end coverage of <c>RawEndpoint&lt;TRequest&gt;</c>, the middle tier at the arity with no
///     response — hand-written binding, inherited dispatch, and a <c>204</c> from the base class.
/// </summary>
public sealed class PurgeTasksTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PurgeTasksTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // The reason this tier is used at all: one comma-separated header becomes a collection, which
    // [FromHeader] cannot express. Both tags have to arrive for both tasks to go.
    [Fact]
    public async Task PurgeTasks_SplitsTheHeaderIntoACollectionAndAnswers204()
    {
        // Arrange
        var client = _factory.CreateClient();
        var first = $"purgeA{Guid.NewGuid():N}";
        var second = $"purgeB{Guid.NewGuid():N}";
        var keep = $"keep{Guid.NewGuid():N}";
        var doomedId = await CreateTaskAsync(client, $"{first} task");
        var alsoDoomedId = await CreateTaskAsync(client, $"{second} task");
        var survivorId = await CreateTaskAsync(client, $"{keep} task");

        var request = new HttpRequestMessage(HttpMethod.Delete, "/ops/tasks");
        request.Headers.TryAddWithoutValidation("X-Purge-Tags", $" {first} , {second} ");

        // Act
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/tasks/{doomedId}", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/tasks/{alsoDoomedId}", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await client.GetAsync($"/tasks/{survivorId}", TestContext.Current.CancellationToken)).StatusCode);
    }

    // The hand-written BindAsync reports through the same accumulating collector the high level uses,
    // so a missing header is a 400 naming the header rather than an empty purge.
    [Fact]
    public async Task PurgeTasks_WithoutTheHeader_Returns400NamingIt()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.DeleteAsync("/ops/tasks", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("X-Purge-Tags", body);
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
}
