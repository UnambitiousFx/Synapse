using Microsoft.AspNetCore.Http;
using UnambitiousFx.Synapse.Endpoints.Builders;

namespace UnambitiousFx.Synapse.Endpoints.Tests;

public sealed class EndpointBuilderTests
{
    [Fact]
    public void Build_WhenRouteComesFromMetadata_UsesTheAttributeRoute()
    {
        // Arrange
        var builder = new EndpointBuilder<string>(new EndpointMetadata(["GET"], "/tasks"));

        // Act
        var configuration = builder.Build();

        // Assert
        Assert.Equal("/tasks", configuration.Route);
        Assert.Equal(["GET"], configuration.HttpMethods);
    }

    [Fact]
    public void Build_WhenConfigureDeclaresTheRoute_UsesIt()
    {
        // Arrange
        var builder = new EndpointBuilder<string>(new EndpointMetadata([], string.Empty));

        // Act
        builder.Post("/tasks/computed");
        var configuration = builder.Build();

        // Assert
        Assert.Equal("/tasks/computed", configuration.Route);
        Assert.Equal(["POST"], configuration.HttpMethods);
    }

    [Fact]
    public void Build_WhenNoRouteAnywhere_Throws()
    {
        // Arrange
        var builder = new EndpointBuilder<string>(new EndpointMetadata([], string.Empty));

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Contains("no route", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Created_WhenConfigured_ProducesA201WithLocation()
    {
        // Arrange
        var builder = new EndpointBuilder<string>(new EndpointMetadata(["POST"], "/tasks"));

        // Act
        builder.Created(value => $"/tasks/{value}");
        var mapper = builder.Build().SuccessMapper;

        // Assert
        Assert.NotNull(mapper);
        var result = Assert.IsAssignableFrom<IStatusCodeHttpResult>(mapper("abc"));
        Assert.Equal(StatusCodes.Status201Created, result.StatusCode);
    }

    [Fact]
    public void Build_WhenChainingMetadataBeforeSuccessMapping_Compiles()
    {
        // Arrange
        var builder = new EndpointBuilder<string>(new EndpointMetadata([], string.Empty));

        // Act
        builder.Post("/things").Tag("Things").Name("CreateThing").Created(value => $"/things/{value}");
        var configuration = builder.Build();

        // Assert
        Assert.Equal("/things", configuration.Route);
        Assert.NotNull(configuration.SuccessMapper);
    }
}
