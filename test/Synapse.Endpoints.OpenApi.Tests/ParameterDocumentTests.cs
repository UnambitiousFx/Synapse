namespace UnambitiousFx.Synapse.Endpoints.OpenApi.Tests;

public sealed class ParameterDocumentTests
{
    [Fact]
    public void AddSynapseEndpoints_WithNullOptions_Throws()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(
            () => OpenApiOptionsExtensions.AddSynapseEndpoints(null!));
    }
}
