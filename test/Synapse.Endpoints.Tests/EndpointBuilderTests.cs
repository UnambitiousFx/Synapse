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

    [Fact]
    public void Build_WhenNoSuccessMapperConfigured_DeclaresNoStatusCode()
    {
        // Arrange
        var builder = new EndpointBuilder<string>(new EndpointMetadata(["GET"], "/tasks"));

        // Act
        var configuration = builder.Build();

        // Assert
        Assert.Null(configuration.DeclaredSuccessStatusCode);
    }

    [Fact]
    public void Build_WhenOkConfigured_DeclaresStatusCode200()
    {
        // Arrange
        var builder = new EndpointBuilder<string>(new EndpointMetadata(["GET"], "/tasks"));

        // Act
        builder.Ok();
        var configuration = builder.Build();

        // Assert
        Assert.Equal(StatusCodes.Status200OK, configuration.DeclaredSuccessStatusCode);
    }

    [Fact]
    public void Build_WhenCreatedConfigured_DeclaresStatusCode201()
    {
        // Arrange
        var builder = new EndpointBuilder<string>(new EndpointMetadata(["POST"], "/tasks"));

        // Act
        builder.Created(value => $"/tasks/{value}");
        var configuration = builder.Build();

        // Assert
        Assert.Equal(StatusCodes.Status201Created, configuration.DeclaredSuccessStatusCode);
    }

    [Fact]
    public void Build_WhenAcceptedConfigured_DeclaresStatusCode202()
    {
        // Arrange
        var builder = new EndpointBuilder<string>(new EndpointMetadata(["POST"], "/tasks"));

        // Act
        builder.Accepted();
        var configuration = builder.Build();

        // Assert
        Assert.Equal(StatusCodes.Status202Accepted, configuration.DeclaredSuccessStatusCode);
    }

    [Fact]
    public void Build_WhenNoContentConfigured_DeclaresStatusCode204()
    {
        // Arrange
        var builder = new EndpointBuilder<string>(new EndpointMetadata(["DELETE"], "/tasks"));

        // Act
        builder.NoContent();
        var configuration = builder.Build();

        // Assert
        Assert.Equal(StatusCodes.Status204NoContent, configuration.DeclaredSuccessStatusCode);
    }

    [Fact]
    public void Build_WhenStatusCodeConfigured_DeclaresThatStatusCode()
    {
        // Arrange
        var builder = new EndpointBuilder<string>(new EndpointMetadata(["POST"], "/tasks"));

        // Act
        builder.StatusCode(StatusCodes.Status418ImATeapot);
        var configuration = builder.Build();

        // Assert
        Assert.Equal(StatusCodes.Status418ImATeapot, configuration.DeclaredSuccessStatusCode);
    }
}
