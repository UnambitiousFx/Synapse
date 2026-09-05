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
}
