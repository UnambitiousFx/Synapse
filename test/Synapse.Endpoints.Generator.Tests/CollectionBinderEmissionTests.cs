namespace UnambitiousFx.Synapse.Endpoints.Generator.Tests;

/// <summary>
///     Covers binding a repeated query or header key into the four supported collection shapes:
///     <c>T[]</c>, <c>List&lt;T&gt;</c>, <c>IReadOnlyList&lt;T&gt;</c> and <c>IEnumerable&lt;T&gt;</c>.
/// </summary>
public sealed class CollectionBinderEmissionTests
{
    [Fact]
    public void Generate_ForAStringArrayQueryProperty_EmitsARepeatedKeyLoop()
    {
        // Arrange
        const string source = """
                              using Microsoft.AspNetCore.Mvc;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record SearchQuery : IRequest<int>
                              {
                                  [FromQuery] public string[] Tags { get; init; } = [];
                              }

                              [Get("/search")]
                              public sealed class SearchEndpoint : Endpoint<SearchQuery, int>;
                              """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert
        Assert.Contains("TryGetQueryValues(context, \"Tags\", out var rawTags)", generated);
        Assert.Contains("listTags.ToArray()", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    [Theory]
    [InlineData("System.Collections.Generic.List<int>")]
    [InlineData("System.Collections.Generic.IReadOnlyList<int>")]
    [InlineData("System.Collections.Generic.IEnumerable<int>")]
    public void Generate_ForAListShapedQueryProperty_HandsTheListOverUnconverted(string propertyType)
    {
        // Arrange — all three take a List<T> by implicit conversion, so one materialization covers them.
        var source = $$"""
                       using Microsoft.AspNetCore.Mvc;
                       using UnambitiousFx.Synapse.Abstractions;
                       using UnambitiousFx.Synapse.Endpoints;

                       namespace TestNs;

                       public sealed record SearchQuery : IRequest<int>
                       {
                           [FromQuery] public {{propertyType}} Sizes { get; init; } = null!;
                       }

                       [Get("/search")]
                       public sealed class SearchEndpoint : Endpoint<SearchQuery, int>;
                       """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert
        Assert.Contains("TryGetQueryValues(context, \"Sizes\", out var rawSizes)", generated);
        Assert.DoesNotContain("listSizes.ToArray()", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    [Fact]
    public void Generate_ForAnEnumElement_ParsesEachElementAndReportsItsIndex()
    {
        // Arrange
        const string source = """
                              using Microsoft.AspNetCore.Mvc;
                              using System.Collections.Generic;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public enum TaskState { Open, Done }

                              public sealed record SearchQuery : IRequest<int>
                              {
                                  [FromQuery(Name = "status")] public List<TaskState> Statuses { get; init; } = [];
                              }

                              [Get("/search")]
                              public sealed class SearchEndpoint : Endpoint<SearchQuery, int>;
                              """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert — the index is in the message, and the loop continues past a bad element.
        Assert.Contains("global::System.Enum.TryParse<global::TestNs.TaskState>", generated);
        Assert.Contains("\" is not a valid TestNs.TaskState.\"", generated);
        Assert.Contains("at index \"", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    [Fact]
    public void Generate_ForANullableCollection_LeavesItNullWhenTheKeyIsAbsent()
    {
        // Arrange — nullability is what distinguishes "absent" from "empty"; a non-nullable
        // collection binds to empty and never reports.
        const string source = """
                              using Microsoft.AspNetCore.Mvc;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record SearchQuery : IRequest<int>
                              {
                                  [FromQuery] public string[]? Tags { get; init; }
                              }

                              [Get("/search")]
                              public sealed class SearchEndpoint : Endpoint<SearchQuery, int>;
                              """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert
        Assert.Contains("var hasTags = false;", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    [Fact]
    public void Generate_ForACollection_NeverReportsTheKeyAsRequired()
    {
        // Arrange
        const string source = """
                              using Microsoft.AspNetCore.Mvc;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record SearchQuery : IRequest<int>
                              {
                                  [FromQuery] public string[] Tags { get; init; } = [];
                              }

                              [Get("/search")]
                              public sealed class SearchEndpoint : Endpoint<SearchQuery, int>;
                              """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert
        Assert.DoesNotContain("The query value is required.", generated);
    }

    [Fact]
    public void Generate_ForARouteBoundCollection_ReportsSyne012InsteadOfCrashing()
    {
        // Arrange — a route segment cannot repeat, so a collection-shaped route property has no
        // collection reader to call. It must fall through to the ordinary "unparsable" diagnostic
        // rather than reach CollectionValueReadEmitter, which has no Route arm and throws.
        const string source = """
                              using Microsoft.AspNetCore.Mvc;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record SearchQuery : IRequest<int>
                              {
                                  [FromRoute] public string[] Ids { get; init; } = [];
                              }

                              [Get("/search/{Ids}")]
                              public sealed class SearchEndpoint : Endpoint<SearchQuery, int>;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.Contains(diagnostics, d => d.Id == "SYNE012");
        Assert.DoesNotContain(diagnostics, d => d.Id == "CS8785");
    }

    [Fact]
    public void Generate_ForARepeatedHeader_ReadsItThroughTryGetHeaderValues()
    {
        // Arrange
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record SearchQuery : IRequest<int>
                              {
                                  [FromHeader("X-Tag")] public string[] Tags { get; init; } = [];
                              }

                              [Get("/search")]
                              public sealed class SearchEndpoint : Endpoint<SearchQuery, int>;
                              """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert
        Assert.Contains("TryGetHeaderValues(context, \"X-Tag\", out var rawTags)", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }
}
