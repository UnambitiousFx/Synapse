namespace UnambitiousFx.Synapse.Endpoints.Generator.Tests;

/// <summary>
///     Covers SYNE015: a <c>[NotBound]</c> property on a message the binder deserializes from the
///     request body is still settable by the caller, because <c>System.Text.Json</c> does not read
///     the attribute. See <c>docs/known-issues/066</c>.
/// </summary>
public sealed class NotBoundDiagnosticTests
{
    [Theory]
    [InlineData("Post")]
    [InlineData("Put")]
    [InlineData("Patch")]
    public void Generate_ForANotBoundPropertyOnABodyCarryingVerb_ReportsSyne015(string verb)
    {
        // Arrange
        var source = $$"""
                       using UnambitiousFx.Synapse.Abstractions;
                       using UnambitiousFx.Synapse.Endpoints;

                       namespace TestNs;

                       public sealed record UpdateThingCommand : IRequest
                       {
                           public string Title { get; init; } = "";

                           [NotBound]
                           public string? ModifiedBy { get; init; }
                       }

                       [{{verb}}("/things")]
                       public sealed class UpdateThingEndpoint : Endpoint<UpdateThingCommand>;
                       """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        var reported = Assert.Single(diagnostics, d => d.Id == "SYNE015");
        Assert.Contains("ModifiedBy", reported.GetMessage());
    }

    // The exclusion does hold on a bodyless verb: nothing deserializes the message there, so
    // [NotBound] alone is sufficient and the warning would be a false positive.
    [Theory]
    [InlineData("Get")]
    [InlineData("Delete")]
    public void Generate_ForANotBoundPropertyOnABodylessVerb_ReportsNoSyne015(string verb)
    {
        // Arrange
        var source = $$"""
                       using UnambitiousFx.Synapse.Abstractions;
                       using UnambitiousFx.Synapse.Endpoints;

                       namespace TestNs;

                       public sealed record ListThingsQuery : IRequest<int>
                       {
                           [NotBound]
                           public string? ModifiedBy { get; init; }
                       }

                       [{{verb}}("/things")]
                       public sealed class ListThingsEndpoint : Endpoint<ListThingsQuery, int>;
                       """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.DoesNotContain(diagnostics, d => d.Id == "SYNE015");
    }

    // An explicit [FromBody] forces a body read even on a bodyless verb, and the emitter's condition
    // is mirrored here — so the warning has to follow it rather than the verb alone.
    [Fact]
    public void Generate_ForANotBoundPropertyBesideAnExplicitFromBodyOnABodylessVerb_ReportsSyne015()
    {
        // Arrange
        const string source = """
                              using Microsoft.AspNetCore.Mvc;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record SearchThingsQuery : IRequest<int>
                              {
                                  [FromBody]
                                  public string Term { get; init; } = "";

                                  [NotBound]
                                  public string? ModifiedBy { get; init; }
                              }

                              [Get("/things")]
                              public sealed class SearchThingsEndpoint : Endpoint<SearchThingsQuery, int>;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.Contains(diagnostics, d => d.Id == "SYNE015");
    }

    [Fact]
    public void Generate_ForANotBoundPropertyCarryingJsonIgnore_ReportsNoSyne015()
    {
        // Arrange — the documented fix.
        const string source = """
                              using System.Text.Json.Serialization;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record UpdateThingCommand : IRequest
                              {
                                  public string Title { get; init; } = "";

                                  [NotBound]
                                  [JsonIgnore]
                                  public string? ModifiedBy { get; init; }
                              }

                              [Post("/things")]
                              public sealed class UpdateThingEndpoint : Endpoint<UpdateThingCommand>;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.DoesNotContain(diagnostics, d => d.Id == "SYNE015");
    }

    // Only JsonIgnoreCondition.Always keeps the serializer from setting the property; the other
    // conditions govern serialization only and leave it writable from the body.
    [Theory]
    [InlineData("JsonIgnoreCondition.Never", true)]
    [InlineData("JsonIgnoreCondition.WhenWritingNull", true)]
    [InlineData("JsonIgnoreCondition.WhenWritingDefault", true)]
    [InlineData("JsonIgnoreCondition.Always", false)]
    public void Generate_ForANotBoundPropertyWithAJsonIgnoreCondition_ReportsSyne015OnlyWhenItStillDeserializes(
        string condition,
        bool expected)
    {
        // Arrange
        var source = $$"""
                       using System.Text.Json.Serialization;
                       using UnambitiousFx.Synapse.Abstractions;
                       using UnambitiousFx.Synapse.Endpoints;

                       namespace TestNs;

                       public sealed record UpdateThingCommand : IRequest
                       {
                           public string Title { get; init; } = "";

                           [NotBound]
                           [JsonIgnore(Condition = {{condition}})]
                           public string? ModifiedBy { get; init; }
                       }

                       [Post("/things")]
                       public sealed class UpdateThingEndpoint : Endpoint<UpdateThingCommand>;
                       """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.Equal(expected, diagnostics.Any(d => d.Id == "SYNE015"));
    }

    // The property must still be excluded from the generated bindings — SYNE015 is an additional
    // warning about a different layer, not a change to what [NotBound] does.
    [Fact]
    public void Generate_ForANotBoundProperty_StillEmitsNoBindingForIt()
    {
        // Arrange — Term binds from the query string by rule 4, so a real binder is emitted and the
        // absence of ModifiedBy below is the exclusion rather than an empty file.
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record ListThingsQuery : IRequest<int>
                              {
                                  public string? Term { get; init; }

                                  [NotBound]
                                  public string? ModifiedBy { get; init; }
                              }

                              [Get("/things")]
                              public sealed class ListThingsEndpoint : Endpoint<ListThingsQuery, int>;
                              """;

        // Act
        var binders = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert
        Assert.Contains("Term", binders);
        Assert.DoesNotContain("ModifiedBy", binders);
    }

    // Found while testing the above, and worth pinning: [NotBound] wins over rule 3, so excluding the
    // only property a route parameter could have matched leaves that parameter unbindable and SYNE001
    // reports it. The exclusion is honoured, and the endpoint is rejected rather than silently losing
    // its route value.
    [Fact]
    public void Generate_WhenNotBoundExcludesTheOnlyRouteParameterMatch_ReportsSyne001()
    {
        // Arrange
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record GetThingQuery : IRequest<int>
                              {
                                  [NotBound]
                                  public string? ThingId { get; init; }
                              }

                              [Get("/things/{thingId}")]
                              public sealed class GetThingEndpoint : Endpoint<GetThingQuery, int>;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.Contains(diagnostics, d => d.Id == "SYNE001");
    }
}
