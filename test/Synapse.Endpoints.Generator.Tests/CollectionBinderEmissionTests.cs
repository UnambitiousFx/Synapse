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
                              public sealed partial class SearchEndpoint : Endpoint<SearchQuery, int>;
                              """;

        // Act
        var generated = GeneratorHarness.GetEndpointFile(source);

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
                       public sealed partial class SearchEndpoint : Endpoint<SearchQuery, int>;
                       """;

        // Act
        var generated = GeneratorHarness.GetEndpointFile(source);

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
                              public sealed partial class SearchEndpoint : Endpoint<SearchQuery, int>;
                              """;

        // Act
        var generated = GeneratorHarness.GetEndpointFile(source);

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
                              public sealed partial class SearchEndpoint : Endpoint<SearchQuery, int>;
                              """;

        // Act
        var generated = GeneratorHarness.GetEndpointFile(source);

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
                              public sealed partial class SearchEndpoint : Endpoint<SearchQuery, int>;
                              """;

        // Act
        var generated = GeneratorHarness.GetEndpointFile(source);

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
                              public sealed partial class SearchEndpoint : Endpoint<SearchQuery, int>;
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
                              public sealed partial class SearchEndpoint : Endpoint<SearchQuery, int>;
                              """;

        // Act
        var generated = GeneratorHarness.GetEndpointFile(source);

        // Assert
        Assert.Contains("TryGetHeaderValues(context, \"X-Tag\", out var rawTags)", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    [Fact]
    public void Generate_ForAFormSourcedCollection_ReadsItThroughTryGetFormValues()
    {
        // Arrange — a form field repeats exactly as a query key does, so the third source the
        // cardinality axis supports is the form. This is the seam between the two slices this branch
        // shipped: collections were built over query and header, the form source arrived afterwards,
        // and their product went untested until now.
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record TagCommand : IRequest
                              {
                                  [FromForm] public string[] Tags { get; init; } = [];
                              }

                              [Post("/tags")]
                              public sealed partial class TagEndpoint : Endpoint<TagCommand>;
                              """;

        // Act
        var generated = GeneratorHarness.GetEndpointFile(source);

        // Assert — and the form is read first, because the field readers serve from its cache.
        Assert.Contains("BindingHelpers.ReadFormAsync(context)", generated);
        Assert.Contains("TryGetFormValues(context, \"Tags\", out var rawTags)", generated);
        Assert.DoesNotContain("TryGetQueryValues", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    [Fact]
    public void Generate_ForAFormSourcedEnumCollection_ReportsABadElementUnderTheFieldName()
    {
        // Arrange
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public enum TaskStatus { Open, Done }

                              public sealed record StatusCommand : IRequest
                              {
                                  [FromForm("status")] public TaskStatus[] Statuses { get; init; } = [];
                              }

                              [Post("/statuses")]
                              public sealed partial class StatusEndpoint : Endpoint<StatusCommand>;
                              """;

        // Act
        var generated = GeneratorHarness.GetEndpointFile(source);

        // Assert — "form value", not "query value": the message names the source it actually read.
        Assert.Contains("TryGetFormValues(context, \"status\", out var rawStatuses)", generated);
        Assert.Contains("The form value at index ", generated);
        Assert.Contains("validation.AddError(\"status\"", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    [Fact]
    public void Generate_ForANullableCollectionInAPrimaryConstructor_BindsNullWhenTheKeyIsAbsent()
    {
        // Arrange — a constructor argument is applied unconditionally, so there is no assignment for a
        // presence flag to guard: the null has to live in the local itself. Without that, an absent
        // key handed the constructor an empty array and "nullable distinguishes absent from empty"
        // held only for a plain settable property.
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record SearchQuery(string[]? Tags) : IRequest<int>;

                              [Get("/search")]
                              public sealed partial class SearchEndpoint : Endpoint<SearchQuery, int>;
                              """;

        // Act
        var generated = GeneratorHarness.GetEndpointFile(source);

        // Assert
        Assert.Contains("string[]? valueTags = default;", generated);
        Assert.DoesNotContain("var valueTags = listTags.ToArray();", generated);
        Assert.Contains("new global::TestNs.SearchQuery(valueTags)", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    [Fact]
    public void Generate_ForARequiredNullableCollectionInAnObjectInitializer_BindsNullWhenTheKeyIsAbsent()
    {
        // Arrange — a `required` member must be set in the object initializer (CS9035), which is the
        // second shape that applies the value unconditionally.
        const string source = """
                              using System.Collections.Generic;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record SearchQuery : IRequest<int>
                              {
                                  public required List<int>? Ids { get; set; }
                              }

                              [Get("/search")]
                              public sealed partial class SearchEndpoint : Endpoint<SearchQuery, int>;
                              """;

        // Act
        var generated = GeneratorHarness.GetEndpointFile(source);

        // Assert
        Assert.Contains("global::System.Collections.Generic.List<int>? valueIds = default;", generated);
        Assert.Contains("{ Ids = valueIds }", generated);
        Assert.DoesNotContain("var valueIds = listIds;", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    [Fact]
    public void Generate_ForANullableCollectionOnASettableProperty_StillGuardsWithAPresenceFlag()
    {
        // Arrange — the one shape that already worked, kept as a regression guard: here the presence
        // flag is what makes an absent key leave the property's own initializer alone, so the local
        // stays non-nullable and the assignment is what is conditional.
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed class SearchQuery : IRequest<int>
                              {
                                  public string[]? Tags { get; set; }
                              }

                              [Get("/search")]
                              public sealed partial class SearchEndpoint : Endpoint<SearchQuery, int>;
                              """;

        // Act
        var generated = GeneratorHarness.GetEndpointFile(source);

        // Assert
        Assert.Contains("var hasTags = false;", generated);
        Assert.Contains("var valueTags = listTags.ToArray();", generated);
        Assert.Contains("if (hasTags)", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }
}
