using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using UnambitiousFx.Examples.MinimalApi;
using UnambitiousFx.Examples.MinimalApi.Features.Tasks;

namespace UnambitiousFx.Examples.MinimalApi.Tests;

public sealed class TasksApiTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public TasksApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    /// <summary>
    ///     Creates a task via POST /tasks and returns the new task's ID parsed from the Location header.
    /// </summary>
    private async Task<Guid> CreateTaskAsync(HttpClient client, string title = "Test Task", string description = "Test description")
    {
        var response = await client.PostAsJsonAsync("/tasks", new { Title = title, Description = description });
        response.EnsureSuccessStatusCode();
        var location = response.Headers.Location;
        Assert.NotNull(location);
        return Guid.Parse(location.ToString().Split('/').Last());
    }

    [Fact]
    public async Task CreateTask_Returns201WithLocation()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/tasks", new { Title = "My Task", Description = "My description" }, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.Contains("/tasks/", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task CreateTask_EmptyTitle_ReturnsFailure()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act — empty title triggers CreateTaskCommandValidator via RequestValidationBehavior
        var response = await client.PostAsJsonAsync("/tasks", new { Title = "", Description = "Some description" }, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task GetTask_AfterCreate_ReturnsMatchingTask()
    {
        // Arrange
        var client = _factory.CreateClient();
        var id = await CreateTaskAsync(client, "Get Me", "Get this task");

        // Act
        var response = await client.GetAsync($"/tasks/{id}", TestContext.Current.CancellationToken);

        // Assert
        response.EnsureSuccessStatusCode();
        var task = await response.Content.ReadFromJsonAsync<TaskDto>(TestContext.Current.CancellationToken);
        Assert.NotNull(task);
        Assert.Equal(id, task.Id);
        Assert.Equal("Get Me", task.Title);
        Assert.Equal("Get this task", task.Description);
    }

    [Fact]
    public async Task ListTasks_ReturnsCreatedTasks()
    {
        // Arrange
        var client = _factory.CreateClient();
        await CreateTaskAsync(client, "Task A", "Description A");
        await CreateTaskAsync(client, "Task B", "Description B");

        // Act
        var response = await client.GetAsync("/tasks", TestContext.Current.CancellationToken);

        // Assert
        response.EnsureSuccessStatusCode();
        var taskList = await response.Content.ReadFromJsonAsync<List<TaskDto>>(TestContext.Current.CancellationToken);
        Assert.NotNull(taskList);
        Assert.NotEmpty(taskList);
    }

    [Fact]
    public async Task UpdateTask_ReturnsSuccess()
    {
        // Arrange
        var client = _factory.CreateClient();
        var id = await CreateTaskAsync(client);

        // Act
        var response = await client.PutAsJsonAsync($"/tasks/{id}", new { Title = "Updated Title", Description = "Updated description" }, TestContext.Current.CancellationToken);

        // Assert
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task CompleteTask_ReturnsSuccess()
    {
        // Arrange
        var client = _factory.CreateClient();
        var id = await CreateTaskAsync(client);

        // Act — no request body; only the route id is needed
        var response = await client.PostAsync($"/tasks/{id}/complete", content: null, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task DeleteTask_ReturnsSuccess()
    {
        // Arrange
        var client = _factory.CreateClient();
        var id = await CreateTaskAsync(client);

        // Act
        var response = await client.DeleteAsync($"/tasks/{id}", TestContext.Current.CancellationToken);

        // Assert
        response.EnsureSuccessStatusCode();
    }

    // ── Value-type (int) response through closed CQRS + AuthorizationBehavior (known-issue 001) ──

    [Fact]
    public async Task Purge_WithAdminPermission_Returns200AndIntCount()
    {
        // Arrange — PurgeCompletedTasksCommand : IRequest<int>; the int response flows through the
        // generated closed CqrsBoundaryEnforcementBehavior<,int> and AuthorizationBehavior<,int>.
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/tasks/admin/purge");
        request.Headers.Add("X-User-Permissions", "tasks:admin");

        // Act
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // Assert — success with a value-type int body (no AOT/DI value-type resolution failure).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var purgedCount = await response.Content.ReadFromJsonAsync<int>(TestContext.Current.CancellationToken);
        Assert.True(purgedCount >= 0);
    }

    [Fact]
    public async Task Purge_WithoutPermission_IsDenied()
    {
        // Arrange — no X-User-Permissions header → AuthorizationBehavior short-circuits.
        var client = _factory.CreateClient();

        // Act
        var response = await client.PostAsync("/tasks/admin/purge", content: null, cancellationToken: TestContext.Current.CancellationToken);

        // Assert — handler never runs; the typed UnauthorizedFailure maps to 401 Unauthorized
        // (DefaultFailureHttpMapper — see known-issue 003). Either 401 or 403 is
        // a correct denial code; the package default for UnauthorizedFailure is 401.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
