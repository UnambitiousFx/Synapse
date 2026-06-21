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
        var response = await client.PostAsJsonAsync("/tasks", new { Title = "My Task", Description = "My description" });

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
        var response = await client.PostAsJsonAsync("/tasks", new { Title = "", Description = "Some description" });

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
        var response = await client.GetAsync($"/tasks/{id}");

        // Assert
        response.EnsureSuccessStatusCode();
        var task = await response.Content.ReadFromJsonAsync<TaskDto>();
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
        var response = await client.GetAsync("/tasks");

        // Assert
        response.EnsureSuccessStatusCode();
        var taskList = await response.Content.ReadFromJsonAsync<List<TaskDto>>();
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
        var response = await client.PutAsJsonAsync($"/tasks/{id}", new { Title = "Updated Title", Description = "Updated description" });

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
        var response = await client.PostAsync($"/tasks/{id}/complete", content: null);

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
        var response = await client.DeleteAsync($"/tasks/{id}");

        // Assert
        response.EnsureSuccessStatusCode();
    }
}
