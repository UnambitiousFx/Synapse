using UnambitiousFx.Synapse.Endpoints.Internal;

namespace UnambitiousFx.Synapse.Endpoints.Tests;

public sealed class BoundParameterMetadataTests
{
    [Fact]
    public void BoundParametersMetadata_WithParameters_ExposesThem()
    {
        // Arrange
        var page = new BoundParameterMetadata
        {
            Name = "page",
            Location = BoundParameterLocation.Query,
            Required = true,
            IsArray = false,
            ValueType = typeof(int)
        };

        // Act
        var metadata = new BoundParametersMetadata([page]);

        // Assert
        Assert.Single(metadata.Parameters);
        Assert.Equal("page", metadata.Parameters[0].Name);
        Assert.Equal(BoundParameterLocation.Query, metadata.Parameters[0].Location);
        Assert.True(metadata.Parameters[0].Required);
        Assert.False(metadata.Parameters[0].IsArray);
        Assert.Equal(typeof(int), metadata.Parameters[0].ValueType);
    }

    [Fact]
    public void BoundParametersMetadata_WithNullParameters_Throws()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new BoundParametersMetadata(null!));
    }

    [Fact]
    public void FormFieldMetadata_ForFilePart_CarriesFormFileAsValueType()
    {
        // Arrange & Act
        var field = new FormFieldMetadata
        {
            Name = "File",
            Required = true,
            IsArray = false,
            ValueType = typeof(Microsoft.AspNetCore.Http.IFormFile)
        };

        // Assert
        Assert.Equal(typeof(Microsoft.AspNetCore.Http.IFormFile), field.ValueType);
        Assert.True(field.Required);
    }
}
