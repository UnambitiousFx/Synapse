using Microsoft.AspNetCore.Http;
using UnambitiousFx.Synapse.Endpoints.Binding;
using UnambitiousFx.Synapse.Endpoints.Builders;

namespace UnambitiousFx.Synapse.Endpoints.Testing.Tests;

public sealed class RoutingDiagnosticsTests
{
    [Fact]
    public async Task SendAsync_WhenTheUrlMatchesNoRoute_ThrowsNamingTheMappedRoute()
    {
        // Arrange
        EndpointRegistry.RegisterMetadata<ItemEndpoint>(new EndpointMetadata(["GET"], "/items/{id:guid}"));
        using var harness = EndpointHarness.Create<ItemEndpoint>();

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Get("/item/nope").SendAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("'/item/nope' matched no route", exception.Message);
        Assert.Contains("GET /items/{id:guid}", exception.Message);
    }

    [Fact]
    public async Task SendAsync_WhenARouteConstraintRejectsTheValue_ThrowsRatherThanReachingTheEndpoint()
    {
        // Arrange: the :guid constraint is the framework's, and it runs here because the harness
        // maps through the real route table rather than seeding route values by hand.
        EndpointRegistry.RegisterMetadata<ItemEndpoint>(new EndpointMetadata(["GET"], "/items/{id:guid}"));
        using var harness = EndpointHarness.Create<ItemEndpoint>();

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Get("/items/not-a-guid").SendAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("matched no route", exception.Message);
    }

    [Fact]
    public async Task SendAsync_WhenTheVerbIsWrong_Returns405()
    {
        // Arrange
        EndpointRegistry.RegisterMetadata<ItemEndpoint>(new EndpointMetadata(["GET"], "/items/{id:guid}"));
        using var harness = EndpointHarness.Create<ItemEndpoint>();

        // Act
        var response = await harness.Post($"/items/{Guid.NewGuid()}").SendAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status405MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task SendAsync_ForAnEndpointInAGroup_AppliesTheGroupPrefix()
    {
        // Arrange
        EndpointRegistry.RegisterMetadata<GroupedEndpoint>(new EndpointMetadata(
            ["GET"], "/ping", typeof(OpsGroup), static () => new OpsGroup()));
        using var harness = EndpointHarness.Create<GroupedEndpoint>();

        // Act
        var response = await harness.Get("/ops/ping").SendAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal("GET /ops/ping", harness.RouteDescription);
    }

    [Fact]
    public async Task RouteDescription_ForAMappedEndpoint_NamesTheVerbAndTemplate()
    {
        // Arrange
        EndpointRegistry.RegisterMetadata<ItemEndpoint>(new EndpointMetadata(["GET"], "/items/{id:guid}"));

        // Act
        using var harness = EndpointHarness.Create<ItemEndpoint>();

        // Assert
        Assert.Equal("GET /items/{id:guid}", harness.RouteDescription);
    }

    private sealed class ItemEndpoint : RawEndpoint
    {
        public override ValueTask<IResult> HandleAsync(HttpContext context,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<IResult>(TypedResults.NoContent());
        }
    }

    private sealed class OpsGroup : EndpointGroup
    {
        public override void Configure(IEndpointGroupBuilder builder)
        {
            builder.Prefix("/ops");
        }
    }

    private sealed class GroupedEndpoint : RawEndpoint
    {
        public override ValueTask<IResult> HandleAsync(HttpContext context,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<IResult>(TypedResults.Ok());
        }
    }
}
