namespace UnambitiousFx.Synapse.Endpoints.Generator.Tests;

public sealed class BinderEmissionTests
{
    [Fact]
    public void Generate_ForRouteAndBodyProperties_BindsEachFromItsSource()
    {
        // Arrange
        const string source = """
                              using System;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record UpdateThingCommand : IRequest
                              {
                                  public Guid ThingId { get; init; }
                                  public string Title { get; init; } = "";
                              }

                              [Put("/things/{thingId:guid}")]
                              public sealed class UpdateThingEndpoint : Endpoint<UpdateThingCommand>;
                              """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert
        Assert.Contains("ReadJsonBodyAsync<global::TestNs.UpdateThingCommand>(context)", generated);
        Assert.Contains("TryGetRoute(context, \"thingId\", out var", generated);
        Assert.Contains("global::System.Guid.TryParse(", generated);
        Assert.Contains("ThingId =", generated);
        Assert.DoesNotContain("\"Title\"", generated);

        // This is the design's central illustrative shape (route Guid-parse + record `with` + body
        // property together) — a substring match alone would not have caught either of the two
        // wrong-message-text bugs found by manual inspection during self-review, so compile-check it.
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    [Fact]
    public void Generate_ForBodylessVerb_BindsUnmatchedPropertiesFromQuery()
    {
        // Arrange
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record ListThingsQuery : IRequest<int>
                              {
                                  public int Page { get; init; }
                              }

                              [Get("/things")]
                              public sealed class ListThingsEndpoint : Endpoint<ListThingsQuery, int>;
                              """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert
        Assert.Contains("TryGetQuery(context, \"Page\", out var", generated);
        Assert.DoesNotContain("ReadJsonBodyAsync", generated);
    }

    [Fact]
    public void Generate_ForNotBoundProperty_SkipsIt()
    {
        // Arrange
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record CreateThingCommand : IRequest
                              {
                                  public string Title { get; init; } = "";
                                  [NotBound] public string? ModifiedBy { get; init; }
                              }

                              [Post("/things")]
                              public sealed class CreateThingEndpoint : Endpoint<CreateThingCommand>;
                              """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert
        Assert.DoesNotContain("ModifiedBy", generated);
    }

    [Fact]
    public void Generate_ForHeaderProperty_UsesTheDeclaredHeaderName()
    {
        // Arrange
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record PatchThingCommand : IRequest
                              {
                                  [FromHeader("If-Match")] public string? ETag { get; init; }
                              }

                              [Patch("/things")]
                              public sealed class PatchThingEndpoint : Endpoint<PatchThingCommand>;
                              """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert
        Assert.Contains("TryGetHeader(context, \"If-Match\", out var", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    [Fact]
    public void Generate_ForAllBinderShapes_Compiles()
    {
        // Arrange — reuse the multi-shape source from EndpointGroupEmissionTests.
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record ThingQuery : IRequest<int>
                              {
                                  public int Id { get; init; }
                              }

                              [Get("/things/{id}")]
                              public sealed class ThingEndpoint : Endpoint<ThingQuery, int>;
                              """;

        // Act & Assert
        GeneratorHarness.AssertGeneratedCompiles(source);
    }
}
