namespace UnambitiousFx.Synapse.Endpoints.Tests;

public sealed class ScaffoldTests
{
    [Fact]
    public void Assembly_WhenLoaded_HasExpectedName()
    {
        // Arrange
        var type = typeof(EndpointBase);

        // Act
        var name = type.Assembly.GetName().Name;

        // Assert
        Assert.Equal("UnambitiousFx.Synapse.Endpoints", name);
    }
}
