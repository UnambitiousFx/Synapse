namespace UnambitiousFx.Synapse.Endpoints.Generator.Tests;

/// <summary>
///     Covers the generator's emission of the <c>Parameters</c> member: which binding sources become
///     OpenAPI parameters (Route, Query, Header), which do not (Form, Body), and the required/array/
///     value-type mapping off each bound property.
/// </summary>
public sealed class ParameterMetadataEmissionTests
{
    [Fact]
    public void Emit_WithRequiredQueryScalar_DeclaresRequiredQueryParameter()
    {
        // Arrange
        const string source = """
            using UnambitiousFx.Synapse.Abstractions;
            using UnambitiousFx.Synapse.Endpoints;

            public sealed record SearchQuery : IRequest<string>
            {
                public required int Page { get; init; }
            }

            [Get("/search")]
            public sealed class SearchEndpoint : Endpoint<SearchQuery, string>;
            """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert
        Assert.Contains("BoundParameterMetadata[] ParametersValue", generated);
        Assert.Contains("Name = \"Page\"", generated);
        Assert.Contains("BoundParameterLocation.Query", generated);
        Assert.Contains("Required = true", generated);
        Assert.Contains("IsArray = false", generated);
        Assert.Contains("ValueType = typeof(int)", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    [Fact]
    public void Emit_WithNullableQueryScalar_DeclaresOptionalParameter()
    {
        // Arrange
        const string source = """
            using UnambitiousFx.Synapse.Abstractions;
            using UnambitiousFx.Synapse.Endpoints;

            public sealed record SearchQuery : IRequest<string>
            {
                public int? Page { get; init; }
            }

            [Get("/search")]
            public sealed class SearchEndpoint : Endpoint<SearchQuery, string>;
            """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert
        Assert.Contains("Required = false", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    [Fact]
    public void Emit_WithRenamedQueryKey_UsesSourceKeyNotPropertyName()
    {
        // Arrange
        const string source = """
            using Microsoft.AspNetCore.Mvc;
            using UnambitiousFx.Synapse.Abstractions;
            using UnambitiousFx.Synapse.Endpoints;

            public sealed record SearchQuery : IRequest<string>
            {
                [FromQuery(Name = "tag")] public string[] Tags { get; init; } = [];
            }

            [Get("/search")]
            public sealed class SearchEndpoint : Endpoint<SearchQuery, string>;
            """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert — the document must advertise the key the binder reads.
        Assert.Contains("Name = \"tag\"", generated);
        Assert.DoesNotContain("Name = \"Tags\"", generated);
        Assert.Contains("IsArray = true", generated);
        Assert.Contains("ValueType = typeof(string)", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    [Fact]
    public void Emit_WithRepeatedQueryKey_DeclaresArrayParameterWithElementValueType()
    {
        // Arrange — a repeated query key whose element type is not string, so ValueType being the
        // *element* type rather than the declared collection type is actually observable.
        const string source = """
            using UnambitiousFx.Synapse.Abstractions;
            using UnambitiousFx.Synapse.Endpoints;

            public sealed record SearchQuery : IRequest<string>
            {
                public int[] Ids { get; init; } = [];
            }

            [Get("/search")]
            public sealed class SearchEndpoint : Endpoint<SearchQuery, string>;
            """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert — the array-ness is carried by IsArray, and ValueType is int, never int[]: a
        // consumer must not have to unwrap the collection type itself.
        Assert.Contains("BoundParameterMetadata[] ParametersValue", generated);
        Assert.Contains("Name = \"Ids\"", generated);
        Assert.Contains("BoundParameterLocation.Query", generated);
        Assert.Contains("IsArray = true", generated);
        Assert.Contains("ValueType = typeof(int)", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    [Fact]
    public void Emit_WithNonNullableQueryCollection_DeclaresOptionalParameter()
    {
        // Arrange — non-nullable, and not `required`, but still not required on the wire: HTTP
        // cannot express zero values under a key, so CollectionValueReadEmitter binds an absent
        // ?tag= to an empty collection rather than reporting a missing value.
        const string source = """
            using UnambitiousFx.Synapse.Abstractions;
            using UnambitiousFx.Synapse.Endpoints;

            public sealed record SearchQuery : IRequest<string>
            {
                public required string[] Tags { get; init; }
            }

            [Get("/search")]
            public sealed class SearchEndpoint : Endpoint<SearchQuery, string>;
            """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert
        Assert.Contains("Required = false", generated);
        Assert.DoesNotContain("Required = true", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    [Fact]
    public void Emit_WithConstructorDefaultedQueryScalar_DeclaresOptionalParameter()
    {
        // Arrange — the parameter's default is what the binder falls back to when ?page= is absent
        // (docs/known-issues/060), so the request succeeds and the parameter is not required.
        const string source = """
            using UnambitiousFx.Synapse.Abstractions;
            using UnambitiousFx.Synapse.Endpoints;

            public sealed record ListUsers(int Page = 1) : IRequest<string>;

            [Get("/users")]
            public sealed class ListUsersEndpoint : Endpoint<ListUsers, string>;
            """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert
        Assert.Contains("Name = \"Page\"", generated);
        Assert.Contains("Required = false", generated);
        Assert.DoesNotContain("Required = true", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    [Fact]
    public void Emit_WithRequiredNullableQueryScalar_DeclaresOptionalParameter()
    {
        // Arrange — C#'s `required` is a creation-site rule (CS9035), not a wire one: the binder
        // binds null for an absent ?sort= and answers 200, so the document must not claim the value
        // is mandatory.
        const string source = """
            using UnambitiousFx.Synapse.Abstractions;
            using UnambitiousFx.Synapse.Endpoints;

            public sealed record SearchQuery : IRequest<string>
            {
                public required string? Sort { get; init; }
            }

            [Get("/search")]
            public sealed class SearchEndpoint : Endpoint<SearchQuery, string>;
            """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert
        Assert.Contains("Required = false", generated);
        Assert.DoesNotContain("Required = true", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    [Fact]
    public void Emit_WithRouteAndHeaderProperties_DeclaresBothLocations()
    {
        // Arrange
        const string source = """
            using UnambitiousFx.Synapse.Abstractions;
            using UnambitiousFx.Synapse.Endpoints;

            public sealed record GetQuery : IRequest<string>
            {
                public required System.Guid TaskId { get; init; }
                [FromHeader("X-Tenant")] public required string Tenant { get; init; }
            }

            [Get("/tasks/{taskId:guid}")]
            public sealed class GetEndpoint : Endpoint<GetQuery, string>;
            """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert
        Assert.Contains("BoundParameterLocation.Path", generated);
        Assert.Contains("BoundParameterLocation.Header", generated);
        Assert.Contains("Name = \"X-Tenant\"", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    [Fact]
    public void Emit_WithNullableGuidQueryScalar_DeclaresUnderlyingType()
    {
        // Arrange
        const string source = """
            using UnambitiousFx.Synapse.Abstractions;
            using UnambitiousFx.Synapse.Endpoints;

            public sealed record SearchQuery : IRequest<string>
            {
                public System.Guid? Owner { get; init; }
            }

            [Get("/search")]
            public sealed class SearchEndpoint : Endpoint<SearchQuery, string>;
            """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert — the underlying type, never Nullable<Guid>: TypeFullName is already unwrapped.
        Assert.Contains("ValueType = typeof(global::System.Guid)", generated);
        Assert.DoesNotContain("Nullable", generated);
        Assert.Contains("Required = false", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    [Fact]
    public void Emit_WithBodyBoundMessage_DeclaresNoParametersMember()
    {
        // Arrange — every property comes from the JSON body, so there is nothing to declare.
        const string source = """
            using UnambitiousFx.Synapse.Abstractions;
            using UnambitiousFx.Synapse.Endpoints;

            public sealed record CreateCommand : IRequest<string>
            {
                public required string Title { get; init; }
            }

            [Post("/tasks")]
            public sealed class CreateEndpoint : Endpoint<CreateCommand, string>;
            """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert — no member at all rather than an empty array: the interface default already says
        // "no parameters", and emitting an empty array would be noise in every body-bound binder.
        Assert.DoesNotContain("ParametersValue", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    [Fact]
    public void Emit_WithFormMessage_DeclaresFieldAndFileParts()
    {
        // Arrange
        const string source = """
            using Microsoft.AspNetCore.Http;
            using UnambitiousFx.Synapse.Abstractions;
            using UnambitiousFx.Synapse.Endpoints;

            public sealed record UploadCommand : IRequest<string>
            {
                public required IFormFile File { get; init; }
                public required string Caption { get; init; }
            }

            [Post("/attachments")]
            public sealed class UploadEndpoint : Endpoint<UploadCommand, string>;
            """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert
        Assert.Contains("FormFieldMetadata[] FormFieldsValue", generated);
        Assert.Contains("Name = \"File\"", generated);
        Assert.Contains("ValueType = typeof(global::Microsoft.AspNetCore.Http.IFormFile)", generated);
        Assert.Contains("Name = \"Caption\"", generated);

        // A form field is a body concern, never a parameter.
        Assert.DoesNotContain("ParametersValue", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    [Fact]
    public void Emit_WithFormFileCollection_DeclaresArrayField()
    {
        // Arrange
        const string source = """
            using Microsoft.AspNetCore.Http;
            using UnambitiousFx.Synapse.Abstractions;
            using UnambitiousFx.Synapse.Endpoints;

            public sealed record UploadCommand : IRequest<string>
            {
                public required IFormFileCollection Files { get; init; }
            }

            [Post("/attachments")]
            public sealed class UploadEndpoint : Endpoint<UploadCommand, string>;
            """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert — and not required: FormFileCollectionValueReadEmitter binds an absent file field
        // to an empty collection, exactly as the other collection shapes do.
        Assert.Contains("IsArray = true", generated);
        Assert.Contains("Required = false", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    [Fact]
    public void Emit_WithNonFormMessage_DeclaresNoFormFieldsMember()
    {
        // Arrange
        const string source = """
            using UnambitiousFx.Synapse.Abstractions;
            using UnambitiousFx.Synapse.Endpoints;

            public sealed record SearchQuery : IRequest<string>
            {
                public required int Page { get; init; }
            }

            [Get("/search")]
            public sealed class SearchEndpoint : Endpoint<SearchQuery, string>;
            """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert
        Assert.DoesNotContain("FormFieldsValue", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }
}
