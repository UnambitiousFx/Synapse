using Microsoft.AspNetCore.Http;
using Microsoft.OpenApi;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Endpoints.Binding;

namespace UnambitiousFx.Synapse.Endpoints.OpenApi.Tests;

public sealed partial class FormSchemaDocumentTests
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

    [Fact]
    public async Task Document_WithFormBinderReportingNoFields_KeepsBothContentTypesWithAnEmptyObjectSchema()
    {
        // Arrange — an endpoint can declare BoundBodyKind.Form without describing any field:
        // DeclaredFormFields() defaults to [] and FormRequestMetadata's constructor documents "may be
        // empty". Skipping the form pass here (the old Fields.Count > 0 guard) left such an endpoint
        // with no requestBody at all, losing both content types that ConsumesMatcherPolicy needs to
        // answer 415 — worse than the empty-schema state this package started from.
        var document = await OpenApiTestHost.GenerateAsync<EmptyFormEndpoint>();

        // Act
        var body = document.Paths["/empty-form"].Operations![HttpMethod.Post].RequestBody!;

        // Assert — both content types survive, and the schema honestly describes "a form, its
        // fields are undescribed" rather than the operation losing its request body entirely.
        foreach (var contentType in new[] { "multipart/form-data", "application/x-www-form-urlencoded" })
        {
            var schema = body.Content![contentType].Schema!;
            Assert.Equal(JsonSchemaType.Object, schema.Type);
            Assert.True(schema.Properties is null || schema.Properties.Count == 0);
        }
    }

    [Fact]
    public async Task Document_WithFormAndJsonEndpointsTogether_KeepsBothOperationsIntact()
    {
        // Arrange & Act — one document, two endpoints. The framework bug this package works around
        // crashed the *entire* document over one form-bound endpoint, and a single-endpoint document
        // cannot prove that stopped happening, or that the fixup's parameter removal is narrow
        // enough to spare a real JSON body parameter sitting in the same document.
        var document = await OpenApiTestHost.GenerateAsync<UploadAttachmentEndpoint, CreateTaskEndpoint>();

        // Assert — the form endpoint still declares its multipart + urlencoded schema...
        var formBody = document.Paths["/attachments"].Operations![HttpMethod.Post].RequestBody!;
        Assert.Equal(JsonSchemaType.Object, formBody.Content!["multipart/form-data"].Schema!.Type);
        Assert.Equal(
            JsonSchemaType.Object,
            formBody.Content["application/x-www-form-urlencoded"].Schema!.Type);

        // ...and the JSON endpoint's real body parameter is completely untouched.
        var jsonBody = document.Paths["/tasks"].Operations![HttpMethod.Post].RequestBody!;
        Assert.True(jsonBody.Content!.ContainsKey("application/json"));
        Assert.False(jsonBody.Content.ContainsKey("multipart/form-data"));
    }

    // internal, not private: the generator (referenced as an analyzer in this project, for the
    // other fixtures) emits registration code for every endpoint in the compilation, route attribute
    // or not, and that generated code needs to see these types.
    internal sealed record EmptyFormCommand : IRequest<string>;

    /// <summary>
    ///     Declares a form body without describing a single field — the "supported low-level
    ///     scenario" the review flagged. Only reachable by hand: a generated binding reports
    ///     <c>Form</c> exactly when some property binds from the form, and then it describes that
    ///     property. So this sits at the hand-written-binding tier and overrides the hook directly.
    /// </summary>
    [Post("/empty-form")]
    internal sealed partial class EmptyFormEndpoint : RawEndpoint<EmptyFormCommand, string>
    {
        protected override RequestBodyKind BoundBodyKind => RequestBodyKind.Form;

        public override ValueTask<BindResult<EmptyFormCommand>> BindAsync(HttpContext context)
        {
            return new(BindResult<EmptyFormCommand>.Success(new EmptyFormCommand()));
        }
    }
}
