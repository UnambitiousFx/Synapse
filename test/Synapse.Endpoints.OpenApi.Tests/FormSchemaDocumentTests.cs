using Microsoft.AspNetCore.Http;
using Microsoft.OpenApi;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Endpoints.Binding;

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

    [Fact]
    public async Task Document_WithFormBinderReportingNoFields_KeepsBothContentTypesWithAnEmptyObjectSchema()
    {
        // Arrange — a hand-written binder can report BodyKind.Form without overriding FormFields;
        // IEndpointBinder<TRequest>.FormFields defaults to [] and FormRequestMetadata's constructor
        // documents "may be empty". Skipping the form pass here (the old Fields.Count > 0 guard)
        // left such an endpoint with no requestBody at all, losing both content types that
        // ConsumesMatcherPolicy needs to answer 415 — worse than the empty-schema state this package
        // started from.
        EndpointRegistry.RegisterBinder(new EmptyFormBinder());
        EndpointRegistry.RegisterMetadata<EmptyFormEndpoint>(new EndpointMetadata(["POST"], "/empty-form"));
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
    // other fixtures) emits a binder for every IRequest<T> in the compilation regardless of route
    // attributes, and that generated code needs to see this type. EndpointRegistry.RegisterBinder
    // below overwrites its module-initializer registration with the hand-written one this test needs.
    internal sealed record EmptyFormCommand : IRequest<string>;

    // BodyKind.Form with FormFields left at its [] default — the "supported low-level scenario"
    // the review flagged: a hand-written binder that reports the shape without describing it.
    private sealed class EmptyFormBinder : IEndpointBinder<EmptyFormCommand>
    {
        public bool ReadsRequestBody => true;

        public RequestBodyKind BodyKind => RequestBodyKind.Form;

        public ValueTask<BindResult<EmptyFormCommand>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<EmptyFormCommand>.Success(new EmptyFormCommand()));
        }
    }

    // internal for the same reason as EmptyFormCommand above: the generator emits registration
    // code for every EndpointBase subclass in the compilation, route attribute or not.
    internal sealed class EmptyFormEndpoint : Endpoint<EmptyFormCommand, string>;
}
