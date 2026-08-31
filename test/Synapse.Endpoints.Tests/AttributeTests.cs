namespace UnambitiousFx.Synapse.Endpoints.Tests;

public sealed class AttributeTests
{
    [Theory]
    [InlineData(typeof(GetAttribute), "GET")]
    [InlineData(typeof(PostAttribute), "POST")]
    [InlineData(typeof(PutAttribute), "PUT")]
    [InlineData(typeof(PatchAttribute), "PATCH")]
    [InlineData(typeof(DeleteAttribute), "DELETE")]
    public void VerbAttribute_WhenConstructed_ExposesItsMethodAndRoute(Type attributeType, string expectedMethod)
    {
        // Arrange
        const string route = "/tasks/{id}";

        // Act
        var attribute = (HttpEndpointAttribute)Activator.CreateInstance(attributeType, route)!;

        // Assert
        Assert.Equal(expectedMethod, attribute.Method);
        Assert.Equal(route, attribute.Route);
    }

    [Fact]
    public void HttpEndpointAttribute_WithCustomVerb_UppercasesTheMethod()
    {
        // Arrange & Act
        var attribute = new HttpEndpointAttribute("head", "/tasks");

        // Assert
        Assert.Equal("HEAD", attribute.Method);
    }

    [Fact]
    public void FromHeaderAttribute_WithNoName_LeavesNameNull()
    {
        // Arrange & Act
        var attribute = new FromHeaderAttribute();

        // Assert
        Assert.Null(attribute.Name);
    }

    [Fact]
    public void EndpointMetadata_WithEmptyRoute_IndicatesConfigureDeclaresIt()
    {
        // Arrange & Act
        var metadata = new EndpointMetadata([], string.Empty);

        // Assert
        Assert.True(metadata.IsRouteDeclaredInConfigure);
    }
}
