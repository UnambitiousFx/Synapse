using Microsoft.AspNetCore.Http;
using UnambitiousFx.Synapse.Endpoints.Binding;
using UnambitiousFx.Synapse.Endpoints.Testing.Assertions;

namespace UnambitiousFx.Synapse.Endpoints.Testing.Tests;

public sealed class AssertionTests
{
    [Fact]
    public async Task Status_WhenItMatches_DoesNotThrow()
    {
        // Arrange
        var response = await Respond<CreatedEndpoint>("/created");

        // Act
        var exception = Record.Exception(() => response.ShouldBe().Status(StatusCodes.Status201Created));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public async Task Status_WhenItDoesNotMatch_ThrowsWithTheActualStatusAndBody()
    {
        // Arrange
        var response = await Respond<ProblemEndpoint>("/problem");

        // Act
        var exception = Assert.Throws<EndpointAssertionException>(
            () => response.ShouldBe().Status(StatusCodes.Status201Created));

        // Assert
        Assert.Contains("Expected status 201 but got 400", exception.Message);
        Assert.Contains("\"taskId\"", exception.Message);
    }

    [Fact]
    public async Task Ok_WhenTheStatusIs200_DoesNotThrow()
    {
        // Arrange
        var response = await Respond<OkEndpoint>("/ok");

        // Act
        var exception = Record.Exception(() => response.ShouldBe().Ok());

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public async Task Json_WhenTheBodyMatches_ReturnsTheDeserializedValue()
    {
        // Arrange
        var response = await Respond<OkEndpoint>("/ok");

        // Act
        var value = response.ShouldBe().Json<string>();

        // Assert
        Assert.Equal("fine", value);
    }

    [Fact]
    public async Task Header_WhenTheHeaderIsMissing_ThrowsNamingIt()
    {
        // Arrange
        var response = await Respond<OkEndpoint>("/ok");

        // Act
        var exception = Assert.Throws<EndpointAssertionException>(
            () => response.ShouldBe().Header("Location", "/somewhere"));

        // Assert
        Assert.Contains("Expected header 'Location'", exception.Message);
    }

    [Fact]
    public async Task ValidationProblem_WithTheExpectedError_DoesNotThrow()
    {
        // Arrange
        var response = await Respond<ProblemEndpoint>("/problem");

        // Act
        var exception = Record.Exception(() => response.ShouldBe()
            .ValidationProblem()
            .WithError("taskId", "The route value is not a valid Guid.")
            .WithErrorCount(1));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public async Task ValidationProblem_WhenTheMessageDiffers_ThrowsShowingWhatWasCollected()
    {
        // Arrange
        var response = await Respond<ProblemEndpoint>("/problem");

        // Act
        var exception = Assert.Throws<EndpointAssertionException>(
            () => response.ShouldBe().ValidationProblem().WithError("taskId", "something else"));

        // Assert
        Assert.Contains("The route value is not a valid Guid.", exception.Message);
    }

    [Fact]
    public async Task ValidationProblem_WhenTheStatusIsNot400_ThrowsSayingSo()
    {
        // Arrange
        var response = await Respond<OkEndpoint>("/ok");

        // Act
        var exception = Assert.Throws<EndpointAssertionException>(
            () => response.ShouldBe().ValidationProblem());

        // Assert
        Assert.Contains("Expected status 400 but got 200", exception.Message);
    }

    private static async Task<EndpointResponse> Respond<TEndpoint>(string url)
        where TEndpoint : EndpointBase, new()
    {
        EndpointRegistry.RegisterMetadata<TEndpoint>(new EndpointMetadata(["GET"], url));
        using var harness = EndpointHarness.Create<TEndpoint>();
        return await harness.Get(url).SendAsync(TestContext.Current.CancellationToken);
    }

    private sealed class OkEndpoint : RawEndpoint
    {
        public override ValueTask<IResult> HandleAsync(HttpContext context,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<IResult>(TypedResults.Ok("fine"));
        }
    }

    private sealed class CreatedEndpoint : RawEndpoint
    {
        public override ValueTask<IResult> HandleAsync(HttpContext context,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<IResult>(TypedResults.Created("/created/1"));
        }
    }

    private sealed class ProblemEndpoint : RawEndpoint
    {
        public override ValueTask<IResult> HandleAsync(HttpContext context,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<IResult>(TypedResults.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["taskId"] = ["The route value is not a valid Guid."]
                }));
        }
    }
}
