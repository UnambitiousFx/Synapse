namespace UnambitiousFx.Synapse.Endpoints.Generator.Tests;

/// <summary>
///     Covers the five binding diagnostics reported by <c>EndpointsGenerator</c> during property
///     resolution: SYNE002 (two properties claim the same input), SYNE007 (a body-only property on a
///     bodyless verb), SYNE011 (a route/query/header-bound property that cannot be assigned), SYNE012
///     (a route/query/header-bound property type with no viable <c>TryParse</c>), and SYNE013 (one
///     message type shared by endpoints with conflicting binding shapes). Each diagnostic gets a test
///     that it fires and a test that it stays silent on the equivalent correct shape.
/// </summary>
public sealed class BindingDiagnosticTests
{
    [Fact]
    public void Generate_WhenTwoPropertiesClaimOneRouteParameter_ReportsSyne002()
    {
        // Arrange — "Id" matches route parameter "id" by convention, and "Identifier" is explicitly
        // pinned to the same route parameter via [FromRoute(Name = "id")].
        const string source = """
                              using Microsoft.AspNetCore.Mvc;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record GetThingQuery : IRequest<int>
                              {
                                  public int Id { get; init; }
                                  [FromRoute(Name = "id")] public int Identifier { get; init; }
                              }

                              [Get("/things/{id}")]
                              public sealed class GetThingEndpoint : Endpoint<GetThingQuery, int>;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.Contains(diagnostics, d => d.Id == "SYNE002");
    }

    [Fact]
    public void Generate_WhenTwoPropertiesClaimTheSameQueryKey_ReportsSyne002()
    {
        // Arrange — "Search" and "Query" both explicitly claim query key "q".
        const string source = """
                              using Microsoft.AspNetCore.Mvc;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record SearchQuery : IRequest<int>
                              {
                                  [FromQuery(Name = "q")] public string? Search { get; init; }
                                  [FromQuery(Name = "q")] public string? Query { get; init; }
                              }

                              [Get("/search")]
                              public sealed class SearchEndpoint : Endpoint<SearchQuery, int>;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.Contains(diagnostics, d => d.Id == "SYNE002");
    }

    [Fact]
    public void Generate_WhenTwoPropertiesHaveExplicitFromBody_ReportsSyne002()
    {
        // Arrange — unlike route/query keys, [FromBody]'s "key" is always the property's own name
        // (see EndpointsGenerator.ResolveSource), so this case cannot be found by grouping on
        // (Source, SourceKey) and needs its own check.
        const string source = """
                              using Microsoft.AspNetCore.Mvc;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record CreateThingCommand : IRequest
                              {
                                  [FromBody] public string Title { get; init; } = "";
                                  [FromBody] public string Description { get; init; } = "";
                              }

                              [Post("/things")]
                              public sealed class CreateThingEndpoint : Endpoint<CreateThingCommand>;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.Contains(diagnostics, d => d.Id == "SYNE002");
    }

    [Fact]
    public void Generate_WhenEveryPropertyClaimsADistinctInput_ReportsNoSyne002()
    {
        // Arrange — "Id" binds from the route, "Name" binds from the query (GET is bodyless); no
        // two properties compete for the same input.
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record GetThingQuery : IRequest<int>
                              {
                                  public int Id { get; init; }
                                  public string Name { get; init; } = "";
                              }

                              [Get("/things/{id}")]
                              public sealed class GetThingEndpoint : Endpoint<GetThingQuery, int>;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.DoesNotContain(diagnostics, d => d.Id == "SYNE002");
    }

    [Fact]
    public void Generate_WhenFromBodyPropertyIsOnAGetEndpoint_ReportsSyne007()
    {
        // Arrange — [FromBody] (Rule 1: explicit wins) forces "Payload" to Body even though GET is a
        // bodyless verb; a GET request never actually carries a body at runtime.
        const string source = """
                              using Microsoft.AspNetCore.Mvc;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record GetThingQuery : IRequest<int>
                              {
                                  [FromBody] public string Payload { get; init; } = "";
                              }

                              [Get("/things")]
                              public sealed class GetThingEndpoint : Endpoint<GetThingQuery, int>;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.Contains(diagnostics, d => d.Id == "SYNE007");
    }

    [Fact]
    public void Generate_WhenFromBodyPropertyIsOnAPostEndpoint_ReportsNoSyne007()
    {
        // Arrange — same shape, but POST does carry a body, so [FromBody] is unremarkable here.
        const string source = """
                              using Microsoft.AspNetCore.Mvc;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record CreateThingCommand : IRequest<int>
                              {
                                  [FromBody] public string Payload { get; init; } = "";
                              }

                              [Post("/things")]
                              public sealed class CreateThingEndpoint : Endpoint<CreateThingCommand, int>;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.DoesNotContain(diagnostics, d => d.Id == "SYNE007");
    }

    [Fact]
    public void Generate_WhenRouteBoundPropertyOnNonRecordHasNoSetter_ReportsSyne011()
    {
        // Arrange — GetThingQuery is a plain class (not a record) with an init-only property bound
        // from the route: neither a direct assignment (no setter) nor a `with` expression (not a
        // record) can apply the parsed value.
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed class GetThingQuery : IRequest<int>
                              {
                                  public int Id { get; init; }
                              }

                              [Get("/things/{id}")]
                              public sealed class GetThingEndpoint : Endpoint<GetThingQuery, int>;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.Contains(diagnostics, d => d.Id == "SYNE011");
    }

    [Fact]
    public void Generate_WhenRouteBoundPropertyOnNonRecordHasASetter_ReportsNoSyne011()
    {
        // Arrange — same shape, but "Id" has a regular settable accessor, so it can be assigned
        // directly without needing a `with` expression at all.
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed class GetThingQuery : IRequest<int>
                              {
                                  public int Id { get; set; }
                              }

                              [Get("/things/{id}")]
                              public sealed class GetThingEndpoint : Endpoint<GetThingQuery, int>;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.DoesNotContain(diagnostics, d => d.Id == "SYNE011");
    }

    [Fact]
    public void Generate_WhenRouteBoundPropertyOnRecordHasNoSetter_ReportsNoSyne011()
    {
        // Arrange — same shape, but as a record: the init-only property can be applied through a
        // `with` expression, so nothing is unassignable here.
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record GetThingQuery : IRequest<int>
                              {
                                  public int Id { get; init; }
                              }

                              [Get("/things/{id}")]
                              public sealed class GetThingEndpoint : Endpoint<GetThingQuery, int>;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.DoesNotContain(diagnostics, d => d.Id == "SYNE011");
    }

    [Fact]
    public void Generate_WhenBodyBoundPropertyOnNonRecordHasNoSetter_ReportsNoSyne011()
    {
        // Arrange — SYNE011 is scoped to route/query/header: a [FromBody] property is populated by
        // JSON-deserializing the whole message in one shot (BinderEmitter never assigns it
        // individually), so an init-only property on a non-record body-bound message is not this
        // diagnostic's concern even though it is genuinely omitted from the resolved property list.
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed class CreateThingCommand : IRequest
                              {
                                  public string Name { get; init; } = "";
                              }

                              [Post("/things")]
                              public sealed class CreateThingEndpoint : Endpoint<CreateThingCommand>;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.DoesNotContain(diagnostics, d => d.Id == "SYNE011");
    }

    [Fact]
    public void Generate_WhenRouteBoundTypeHasNoTryParse_ReportsSyne012()
    {
        // Arrange
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed class Unparsable;

                              public sealed record OddQuery : IRequest<int>
                              {
                                  public Unparsable Thing { get; init; } = new();
                              }

                              [Get("/odd/{thing}")]
                              public sealed class OddEndpoint : Endpoint<OddQuery, int>;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.Contains(diagnostics, d => d.Id == "SYNE012");
    }

    [Theory]
    [InlineData("int")]
    [InlineData("long")]
    [InlineData("double")]
    [InlineData("bool")]
    [InlineData("decimal")]
    [InlineData("System.Guid")]
    [InlineData("System.DateTime")]
    [InlineData("System.DateTimeOffset")]
    [InlineData("System.TimeSpan")]
    [InlineData("System.DateOnly")]
    [InlineData("System.TimeOnly")]
    [InlineData("System.DayOfWeek")] // an enum
    [InlineData("string")]
    public void Generate_WhenRouteBoundTypeIsInTheKnownParseableSet_ReportsNoSyne012(string typeName)
    {
        // Arrange — every type SYNE012's brief calls out as known-good, so the diagnostic can never
        // false-positive on a shape the emitter already knows how to bind. "Thing" always binds from
        // the route ("{thing}" matches by name); for `string` no TryParse is needed at all.
        var source = $$"""
                        using UnambitiousFx.Synapse.Abstractions;
                        using UnambitiousFx.Synapse.Endpoints;

                        namespace TestNs;

                        public sealed record OddQuery : IRequest<int>
                        {
                            public {{typeName}} Thing { get; init; } = default!;
                        }

                        [Get("/odd/{thing}")]
                        public sealed class OddEndpoint : Endpoint<OddQuery, int>;
                        """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.DoesNotContain(diagnostics, d => d.Id == "SYNE012");
    }

    [Fact]
    public void Generate_ForTwoEndpointsSharingATypeWithDifferentResolvedBindings_ReportsSyne013()
    {
        // Arrange — SharedCommand's "Value" resolves to Route "value" for AEndpoint (GET, route
        // {value}) but to Body for BEndpoint (POST, no route parameter): genuinely different resolved
        // bindings, not merely a different-looking route/verb that happens to bind the same way.
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record SharedCommand : IRequest
                              {
                                  public string Value { get; init; } = "";
                              }

                              [Get("/a/{value}")]
                              public sealed class AEndpoint : Endpoint<SharedCommand>;

                              [Post("/b")]
                              public sealed class BEndpoint : Endpoint<SharedCommand>;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert — reported once for the shared type, not once per endpoint, and names both.
        var matches = diagnostics.Where(d => d.Id == "SYNE013").ToArray();
        var diagnostic = Assert.Single(matches);
        var message = diagnostic.GetMessage();
        Assert.Contains("SharedCommand", message);
        Assert.Contains("AEndpoint", message);
        Assert.Contains("BEndpoint", message);
    }

    [Fact]
    public void Generate_ForTwoEndpointsSharingATypeWithIdenticalResolvedBindings_ReportsNoSyne013()
    {
        // Arrange — both endpoints are GET with a route template that does not reference "Value" at
        // all, so "Value" resolves to Query for both: the routes and verbs are literally identical
        // here, but the point is the same even when they are not — what matters is whether the
        // *resolved* binding differs, not the raw route/verb text. This is the false-positive shape
        // called out in the brief: two endpoints sharing a type whose bindings happen to agree.
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record SharedQuery : IRequest<int>
                              {
                                  public string Value { get; init; } = "";
                              }

                              [Get("/a")]
                              public sealed class AEndpoint : Endpoint<SharedQuery, int>;

                              [Get("/b")]
                              public sealed class BEndpoint : Endpoint<SharedQuery, int>;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.DoesNotContain(diagnostics, d => d.Id == "SYNE013");
    }

    [Fact]
    public void Generate_ForOneEndpointBindingAType_ReportsNoSyne013()
    {
        // Arrange — a type used by exactly one endpoint can never conflict with itself.
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record GetThingQuery : IRequest<int>
                              {
                                  public int Id { get; init; }
                              }

                              [Get("/things/{id}")]
                              public sealed class GetThingEndpoint : Endpoint<GetThingQuery, int>;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.DoesNotContain(diagnostics, d => d.Id == "SYNE013");
    }
}
