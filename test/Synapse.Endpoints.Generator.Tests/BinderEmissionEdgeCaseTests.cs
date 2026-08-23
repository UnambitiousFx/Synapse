namespace UnambitiousFx.Synapse.Endpoints.Generator.Tests;

/// <summary>
///     Covers binder-emission rules the brief calls out explicitly but that
///     <see cref="BinderEmissionTests" /> does not exercise on its own: the settable-property
///     (non-record) assignment form, a nullable value that is missing rather than invalid, enum
///     parsing, and the two "omit rather than emit code that won't compile" cases (an unassignable
///     init-only property on a non-record, and a property type with no viable <c>TryParse</c>).
/// </summary>
public sealed class BinderEmissionEdgeCaseTests
{
    [Fact]
    public void Generate_ForSettableClassProperty_AssignsDirectlyInsteadOfUsingWith()
    {
        // Arrange — a plain (non-record) class with regular setters; GET is a bodyless verb, so the
        // unmatched "Age" property resolves to Query rather than Body.
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed class SettableCommand : IRequest
                              {
                                  public string Name { get; set; } = "";
                                  public int? Age { get; set; }
                              }

                              [Get("/settables/{name}")]
                              public sealed class SettableEndpoint : Endpoint<SettableCommand>;
                              """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert — direct assignment, not a `with` expression, and a bodyless GET with a body-less
        // property set skips ReadJsonBodyAsync entirely.
        Assert.Contains("message.Name = rawName!;", generated);
        Assert.DoesNotContain("with { Name", generated);
        Assert.DoesNotContain("ReadJsonBodyAsync", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    [Fact]
    public void Generate_ForMissingNullableQueryValue_SkipsAssignmentInsteadOfFailing()
    {
        // Arrange — same shape as above: "Age" is a nullable query value.
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed class SettableCommand : IRequest
                              {
                                  public string Name { get; set; } = "";
                                  public int? Age { get; set; }
                              }

                              [Get("/settables/{name}")]
                              public sealed class SettableEndpoint : Endpoint<SettableCommand>;
                              """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert — the missing-value check for "Age" has no else/failure branch attached to it.
        Assert.Contains("if (global::UnambitiousFx.Synapse.Endpoints.Binding.BindingHelpers.TryGetQuery(context, \"Age\", out var rawAge))",
            generated);
        Assert.DoesNotContain("Query value 'Age' is missing", generated);
    }

    [Fact]
    public void Generate_ForEnumRouteProperty_ParsesThroughEnumTryParse()
    {
        // Arrange — enums have no TryParse of their own; parsing goes through System.Enum instead.
        const string source = """
                              using System;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record DayQuery : IRequest<int>
                              {
                                  public DayOfWeek Day { get; init; }
                              }

                              [Get("/days/{day}")]
                              public sealed class DayEndpoint : Endpoint<DayQuery, int>;
                              """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert
        Assert.Contains("global::System.Enum.TryParse<global::System.DayOfWeek>(rawDay, out var valueDay)", generated);
        Assert.Contains("is not a valid System.DayOfWeek", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    [Fact]
    public void Generate_ForInitOnlyPropertyOnNonRecord_OmitsItButStillReadsTheBody()
    {
        // Arrange — an init-only property on a plain class can be assigned neither via a setter nor
        // via `with` (not a record), so it must be omitted rather than emitted as broken code
        // (Task 17's SYNE011 reports this at the source). POST is not a bodyless verb, so the body
        // must still be read even though nothing here resolved to a Body-sourced property.
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed class InitOnlyClassCommand : IRequest
                              {
                                  public string Name { get; init; } = "";
                              }

                              [Post("/initonly")]
                              public sealed class InitOnlyEndpoint : Endpoint<InitOnlyClassCommand>;
                              """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert
        Assert.DoesNotContain("Name", generated);
        Assert.Contains("ReadJsonBodyAsync<global::TestNs.InitOnlyClassCommand>(context)", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    [Fact]
    public void Generate_ForMvcBindingAttributes_ResolvesRouteQueryAndBodySources()
    {
        // Arrange — [FromRoute]/[FromQuery]/[FromBody] come from Microsoft.AspNetCore.Mvc, distinct
        // from our own [FromHeader]; an explicit name overrides the property's own name as the key.
        const string source = """
                              using Microsoft.AspNetCore.Mvc;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record MixedCommand : IRequest
                              {
                                  [FromRoute(Name = "id")] public int ThingId { get; init; }
                                  [FromQuery(Name = "q")] public string? Search { get; init; }
                                  [FromBody] public string Payload { get; init; } = "";
                              }

                              [Get("/mixed/{id}")]
                              public sealed class MixedEndpoint : Endpoint<MixedCommand>;
                              """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert
        Assert.Contains("TryGetRoute(context, \"id\", out var rawThingId)", generated);
        Assert.Contains("TryGetQuery(context, \"q\", out var rawSearch)", generated);
        Assert.DoesNotContain("Payload", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    [Fact]
    public void Generate_ForPropertyTypeWithNoTryParse_OmitsItWithoutBreakingTheOthers()
    {
        // Arrange — "Thing" has no viable parse path (Task 17's SYNE012); "Id" does and must still
        // be bound correctly alongside it.
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed class NoParseThing
                              {
                              }

                              public sealed record NoTryParseQuery : IRequest<int>
                              {
                                  public NoParseThing? Thing { get; init; }
                                  public int Id { get; init; }
                              }

                              [Get("/noparse")]
                              public sealed class NoTryParseEndpoint : Endpoint<NoTryParseQuery, int>;
                              """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert
        Assert.DoesNotContain("Thing", generated);
        Assert.Contains("int.TryParse(rawId, out var valueId)", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }
}
