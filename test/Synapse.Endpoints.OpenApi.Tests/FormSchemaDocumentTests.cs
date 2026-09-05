using Microsoft.OpenApi;

namespace UnambitiousFx.Synapse.Endpoints.OpenApi.Tests;

public sealed class FormSchemaDocumentTests
{
    [Fact]
    public async Task Document_WithFormMessage_DeclaresFieldSchemaForBothContentTypes()
    {
        // Arrange
        var document = await OpenApiTestHost.GenerateAsync<UploadAttachmentEndpoint>();

        // Act
        var body = document.Paths["/attachments"].Operations![HttpMethod.Post].RequestBody!;

        // Assert — both content types, same schema. The content types are what
        // ConsumesMatcherPolicy needs for 415; the schema is what a client generator needs.
        foreach (var contentType in new[] { "multipart/form-data", "application/x-www-form-urlencoded" })
        {
            var schema = body.Content![contentType].Schema!;
            Assert.Equal(JsonSchemaType.Object, schema.Type);
            Assert.Contains("Caption", schema.Properties!.Keys);
            Assert.Contains("File", schema.Properties.Keys);
            Assert.Contains("File", schema.Required!);
            Assert.Contains("Caption", schema.Required!);
        }
    }

    [Fact]
    public async Task Document_WithFormFile_DeclaresBinaryStringField()
    {
        // Arrange
        var document = await OpenApiTestHost.GenerateAsync<UploadAttachmentEndpoint>();

        // Act
        var schema = document.Paths["/attachments"].Operations![HttpMethod.Post]
            .RequestBody!.Content!["multipart/form-data"].Schema!;

        // Assert
        var file = schema.Properties!["File"];
        Assert.Equal(JsonSchemaType.String, file.Type);
        Assert.Equal("binary", file.Format);
    }

    [Fact]
    public async Task Document_WithFormFileCollection_DeclaresArrayOfBinaryStrings()
    {
        // Arrange
        var document = await OpenApiTestHost.GenerateAsync<UploadManyEndpoint>();

        // Act
        var schema = document.Paths["/attachments/many"].Operations![HttpMethod.Post]
            .RequestBody!.Content!["multipart/form-data"].Schema!;

        // Assert
        var files = schema.Properties!["Files"];
        Assert.Equal(JsonSchemaType.Array, files.Type);
        Assert.Equal(JsonSchemaType.String, files.Items!.Type);
        Assert.Equal("binary", files.Items.Format);
    }

    [Fact]
    public async Task Document_WithJsonMessage_KeepsItsJsonSchema()
    {
        // Arrange & Act — a JSON-bound endpoint must be untouched by the form pass.
        var document = await OpenApiTestHost.GenerateAsync<CreateTaskEndpoint>();

        // Assert
        var body = document.Paths["/tasks"].Operations![HttpMethod.Post].RequestBody!;
        Assert.True(body.Content!.ContainsKey("application/json"));
        Assert.False(body.Content.ContainsKey("multipart/form-data"));
    }
}
