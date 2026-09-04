namespace UnambitiousFx.Synapse.Endpoints.Generator.Tests;

/// <summary>
///     Covers the two ways a collection-shaped property can fail to bind: an unsupported collection
///     type (SYNE016) and a supported one over an unparsable element (SYNE012, naming the element).
/// </summary>
public sealed class CollectionDiagnosticTests
{
    [Fact]
    public void Generate_ForAnUnsupportedCollectionType_ReportsSyne016NamingTheSupportedShapes()
    {
        // Arrange — HashSet<T> is a collection, but not one of the four shapes that bind.
        const string source = """
                              using Microsoft.AspNetCore.Mvc;
                              using System.Collections.Generic;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record SearchQuery : IRequest<int>
                              {
                                  [FromQuery] public HashSet<string> Tags { get; init; } = [];
                              }

                              [Get("/search")]
                              public sealed class SearchEndpoint : Endpoint<SearchQuery, int>;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert — the advice must be followable, which "add a TryParse to HashSet<string>" is not.
        var reported = Assert.Single(diagnostics, d => d.Id == "SYNE016");
        Assert.Contains("IReadOnlyList<string>", reported.GetMessage());
        Assert.DoesNotContain(diagnostics, d => d.Id == "SYNE012");
    }

    [Fact]
    public void Generate_ForASupportedCollectionOfAnUnparsableElement_ReportsSyne012NamingTheElement()
    {
        // Arrange
        const string source = """
                              using Microsoft.AspNetCore.Mvc;
                              using System.Collections.Generic;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed class Untyped { }

                              public sealed record SearchQuery : IRequest<int>
                              {
                                  [FromQuery] public List<Untyped> Items { get; init; } = [];
                              }

                              [Get("/search")]
                              public sealed class SearchEndpoint : Endpoint<SearchQuery, int>;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert — the element, not the collection, is what has no parse path.
        var reported = Assert.Single(diagnostics, d => d.Id == "SYNE012");
        Assert.Contains("TestNs.Untyped", reported.GetMessage());
        Assert.DoesNotContain("List<", reported.GetMessage());
    }

    [Fact]
    public void Generate_ForAJaggedArray_ReportsSyne012NamingTheInnerArrayNotSyne016()
    {
        // Arrange — pinned deliberately, not a bug: IArrayTypeSymbol.Rank reflects only the
        // outermost dimension, so string[][] matches TryResolveCollectionShape's `Rank: 1` check for
        // the supported T[] shape, with element type string[]. It never reaches the SYNE016 branch;
        // it falls through as a supported shape over an unparsable element (string[] has no
        // TryParse), so it reports SYNE012 naming string[] — not SYNE016, which would have to name
        // string[][] as the "unsupported" collection and string[] as one of the shapes to switch to,
        // which is nonsensical since string[][] already IS almost that shape.
        const string source = """
                              using Microsoft.AspNetCore.Mvc;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record SearchQuery : IRequest<int>
                              {
                                  [FromQuery] public string[][] Pages { get; init; } = [];
                              }

                              [Get("/search")]
                              public sealed class SearchEndpoint : Endpoint<SearchQuery, int>;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        var reported = Assert.Single(diagnostics, d => d.Id == "SYNE012");
        Assert.Contains("string[]", reported.GetMessage());
        Assert.DoesNotContain(diagnostics, d => d.Id == "SYNE016");
    }

    [Fact]
    public void Generate_ForAParsableEnumerableType_StillBindsAsAScalar()
    {
        // Arrange — a type that is both enumerable and parsable keeps binding as the scalar it has
        // always been. This is what pins the order of the two checks.
        const string source = """
                              using Microsoft.AspNetCore.Mvc;
                              using System.Collections;
                              using System.Collections.Generic;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed class Csv : IEnumerable<string>
                              {
                                  public static bool TryParse(string? s, out Csv value) { value = new Csv(); return s is not null; }
                                  public IEnumerator<string> GetEnumerator() => throw new System.NotImplementedException();
                                  IEnumerator IEnumerable.GetEnumerator() => throw new System.NotImplementedException();
                              }

                              public sealed record SearchQuery : IRequest<int>
                              {
                                  [FromQuery] public Csv Tags { get; init; } = new();
                              }

                              [Get("/search")]
                              public sealed class SearchEndpoint : Endpoint<SearchQuery, int>;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert
        Assert.DoesNotContain(diagnostics, d => d.Id is "SYNE012" or "SYNE016");
        Assert.Contains("TryGetQuery(context, \"Tags\", out var rawTags)", generated);
        Assert.DoesNotContain("TryGetQueryValues", generated);
    }

    [Fact]
    public void Generate_ForAStringProperty_IsNeverTreatedAsACollectionOfChar()
    {
        // Arrange — string is IEnumerable<char>, which is why it is excluded explicitly.
        const string source = """
                              using Microsoft.AspNetCore.Mvc;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record SearchQuery : IRequest<int>
                              {
                                  [FromQuery] public string Term { get; init; } = "";
                              }

                              [Get("/search")]
                              public sealed class SearchEndpoint : Endpoint<SearchQuery, int>;
                              """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert
        Assert.Contains("TryGetQuery(context, \"Term\", out var rawTerm)", generated);
        Assert.DoesNotContain("TryGetQueryValues", generated);
    }
}
