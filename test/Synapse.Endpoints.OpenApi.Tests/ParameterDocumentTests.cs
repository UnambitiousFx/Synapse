using Microsoft.OpenApi;

namespace UnambitiousFx.Synapse.Endpoints.OpenApi.Tests;

public sealed class ParameterDocumentTests
{
    [Fact]
    public async Task Document_WithRequiredQueryScalar_DeclaresRequiredQueryParameter()
    {
        // Arrange
        var document = await OpenApiTestHost.GenerateAsync<SearchTasksEndpoint>();

        // Act — "Page", not "page": a query property with no [FromQuery] is bound by the
        // bodyless-verb convention under its own property name, and the document has to declare the
        // key the binder actually reads. "tag" below is the renamed case.
        var parameter = document.Paths["/tasks/search"]
            .Operations![HttpMethod.Get]
            .Parameters!
            .Single(p => p.Name == "Page");

        // Assert — "integer" or "string", not plain "integer": the web JSON defaults a host gets
        // from CreateSlimBuilder set NumberHandling.AllowReadingFromString, and the schema comes
        // from the document's own generator, so a number really is documented as accepting both.
        // That is the whole point of routing through GetOrCreateSchemaAsync rather than a local
        // Type-to-schema table, which would contradict the response bodies in the same document.
        Assert.Equal(ParameterLocation.Query, parameter.In);
        Assert.True(parameter.Required);
        Assert.Equal(JsonSchemaType.Integer | JsonSchemaType.String, parameter.Schema!.Type);
    }

    [Fact]
    public async Task Document_WithQueryCollection_DeclaresArrayParameter()
    {
        // Arrange
        var document = await OpenApiTestHost.GenerateAsync<SearchTasksEndpoint>();

        // Act
        var parameter = document.Paths["/tasks/search"]
            .Operations![HttpMethod.Get]
            .Parameters!
            .Single(p => p.Name == "tag");

        // Assert — required: false even though `string[] Tags` is non-nullable: HTTP cannot express
        // zero values under a key, so an absent ?tag= binds an empty collection rather than
        // failing, and a parameter the binder never rejects is not a required one.
        Assert.Equal(JsonSchemaType.Array, parameter.Schema!.Type);
        Assert.Equal(JsonSchemaType.String, parameter.Schema.Items!.Type);
        Assert.False(parameter.Required);
    }

    [Fact]
    public async Task Document_WithRouteProperty_DeclaresExactlyOnePathParameter()
    {
        // Arrange
        var document = await OpenApiTestHost.GenerateAsync<GetTaskEndpoint>();

        // Act
        var parameters = document.Paths["/tasks/{taskId}"]
            .Operations![HttpMethod.Get]
            .Parameters!
            .Where(p => p.Name == "taskId")
            .ToList();

        // Assert — reconciled, not duplicated. A second entry is invalid OpenAPI.
        Assert.Single(parameters);
        Assert.Equal(ParameterLocation.Path, parameters[0].In);
        Assert.NotNull(parameters[0].Schema);
    }

    [Fact]
    public async Task Document_WithNullableRouteProperty_StillDeclaresPathParameterRequired()
    {
        // Arrange — PeekTaskQuery.TaskId is Guid?, so the binder accepts its absence and reports
        // Required = false. OpenAPI 3.x forbids an optional path parameter, so the transformer has
        // to override that rather than emit an invalid document.
        var document = await OpenApiTestHost.GenerateAsync<PeekTaskEndpoint>();

        // Act
        var parameter = document.Paths["/tasks/{taskId}/peek"]
            .Operations![HttpMethod.Get]
            .Parameters!
            .Single(p => p.Name == "taskId");

        // Assert
        Assert.Equal(ParameterLocation.Path, parameter.In);
        Assert.True(parameter.Required);
    }

    [Fact]
    public async Task Document_WithHeaderProperty_DeclaresHeaderParameter()
    {
        // Arrange
        var document = await OpenApiTestHost.GenerateAsync<GetTaskEndpoint>();

        // Act
        var parameter = document.Paths["/tasks/{taskId}"]
            .Operations![HttpMethod.Get]
            .Parameters!
            .Single(p => p.Name == "X-Tenant");

        // Assert
        Assert.Equal(ParameterLocation.Header, parameter.In);
        Assert.True(parameter.Required);
        Assert.Equal(JsonSchemaType.String, parameter.Schema!.Type);
    }

    [Fact]
    public async Task Document_WithHandWrittenMapGet_IsUntouched()
    {
        // Arrange & Act — a route Synapse did not map must gain nothing.
        var document = await OpenApiTestHost.GenerateWithHandWrittenRouteAsync();

        // Assert
        var operation = document.Paths["/version"].Operations![HttpMethod.Get];
        Assert.True(operation.Parameters is null || operation.Parameters.Count == 0);
    }
}
