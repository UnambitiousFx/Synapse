using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Examples.EndpointsApi.Features.Tasks;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Endpoints.Testing;
using UnambitiousFx.Synapse.Endpoints.Testing.Assertions;

namespace UnambitiousFx.Examples.EndpointsApi.Tests;

/// <summary>
///     The harness against real endpoints and generator-produced binders. The harness's own tests
///     register binders by hand, so this is the only place the analyzer's output is exercised
///     through it.
/// </summary>
public sealed class HarnessTests
{
    [Fact]
    public async Task GetTask_WhenTheHandlerReportsNotFound_Returns404()
    {
        // Arrange
        using var harness = CreateHarness<GetTaskEndpoint>(options =>
            options.Handle<GetTaskQuery, TaskDto>(
                query => Result.FailNotFound<TaskDto>("Task", query.TaskId.ToString())));

        // Act
        var response = await harness.Get($"/tasks/{Guid.NewGuid()}").SendAsync(TestContext.Current.CancellationToken);

        // Assert
        response.ShouldBe().NotFound();
    }

    [Fact]
    public async Task GetTask_WithAGeneratedBinder_BindsTheRouteValueOntoTheQuery()
    {
        // Arrange
        var taskId = Guid.NewGuid();
        Guid? bound = null;
        using var harness = CreateHarness<GetTaskEndpoint>(options =>
            options.Handle<GetTaskQuery, TaskDto>(query =>
            {
                bound = query.TaskId;
                return Result.Success(new TaskDto { Id = query.TaskId, Title = "Bound" });
            }));

        // Act
        var response = await harness.Get($"/tasks/{taskId}").SendAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(taskId, bound);
        Assert.Equal(taskId, response.ShouldBe().Ok().Json<TaskDto>().Id);
    }

    [Fact]
    public async Task GetTask_WhenTheIdIsNotAGuid_NeverReachesTheEndpoint()
    {
        // Arrange: the route is /tasks/{taskId:guid}, so the constraint rejects this before binding.
        using var harness = CreateHarness<GetTaskEndpoint>(static _ => { });

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Get("/tasks/not-a-guid").SendAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("GET /tasks/{taskId:guid}", exception.Message);
    }

    [Fact]
    public async Task CreateTask_WithABody_Returns201WithTheConfiguredLocation()
    {
        // Arrange
        var taskId = Guid.NewGuid();
        using var harness = CreateHarness<CreateTaskEndpoint>(options =>
            options.Handle<CreateTaskCommand, TaskCreated>(
                _ => Result.Success(new TaskCreated { TaskId = taskId })));

        // Act
        var response = await harness.Post("/tasks/")
            .JsonBody(new CreateTaskCommand { Title = "Write the harness" })
            .SendAsync(TestContext.Current.CancellationToken);

        // Assert
        response.ShouldBe()
            .Created()
            .Header("Location", $"/tasks/{taskId}");
    }

    [Fact]
    public async Task CreateTask_WithNoTitle_Returns400WithTheParseErrorNamingTheProperty()
    {
        // Arrange: the binding half, which before the harness needed a request over a socket.
        using var harness = CreateHarness<CreateTaskEndpoint>(static _ => { });

        // Act
        var response = await harness.Post("/tasks/").Body("{}", "application/json").SendAsync(TestContext.Current.CancellationToken);

        // Assert: the generated binder does not key this as "title" or "Title" — a required-property
        // gap surfaces as a JsonException from the source-generated deserializer, which the binder
        // reports as a single "body" error whose message names the missing property. Verified against
        // the generated binder for CreateTaskCommand; the key genuinely is "body", not the field name.
        var problem = response.ShouldBe()
            .ValidationProblem()
            .WithErrorFor("body")
            .Problem;
        Assert.Contains("title", problem.Errors["body"][0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task StreamTasks_WithoutAnAcceptHeader_MaterialisesAJsonArray()
    {
        // Arrange
        using var harness = CreateHarness<StreamTasksEndpoint>(options =>
            options.HandleStream<StreamTasksQuery, TaskDto>(_ =>
            [
                new TaskDto { Id = Guid.NewGuid(), Title = "First" },
                new TaskDto { Id = Guid.NewGuid(), Title = "Second" }
            ]));

        // Act
        var response = await harness.Get("/tasks/stream").SendAsync(TestContext.Current.CancellationToken);

        // Assert
        response.ShouldBe().Ok();
        Assert.Equal(2, response.ShouldBe().Json<TaskDto[]>().Length);
    }

    /// <summary>
    ///     Creates a harness carrying the same JSON configuration Program.cs installs, so the
    ///     generated binders read the body through the application's own serializer context rather
    ///     than through whatever the default resolver happens to offer.
    /// </summary>
    private static EndpointHarness<TEndpoint> CreateHarness<TEndpoint>(
        Action<EndpointHarnessOptions> configure)
        where TEndpoint : UnambitiousFx.Synapse.Endpoints.EndpointBase, new()
    {
        return EndpointHarness.Create<TEndpoint>(options =>
        {
            options.Services.ConfigureHttpJsonOptions(json =>
                json.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default));
            configure(options);
        });
    }
}
