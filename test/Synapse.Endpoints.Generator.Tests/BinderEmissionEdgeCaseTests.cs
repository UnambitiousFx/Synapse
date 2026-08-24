namespace UnambitiousFx.Synapse.Endpoints.Generator.Tests;

/// <summary>
///     Covers binder-emission rules the brief calls out explicitly but that
///     <see cref="BinderEmissionTests" /> does not exercise on its own: the settable-property
///     (non-record) assignment form, a nullable value that is missing rather than invalid, enum
///     parsing, and the two "omit rather than emit code that won't compile" cases (an unassignable
///     init-only property on a non-record, and a property type with no viable <c>TryParse</c>).
///     Also covers which verbs count as bodyless — including the "no verb at all" case of an endpoint
///     that declares its route in <c>Configure</c>, which used to emit a body read and fail every
///     request.
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
    public void Generate_ForPositionalRecordWithBodylessVerb_ConstructsThroughThePrimaryConstructor()
    {
        // Arrange — a positional record has no parameterless constructor, so `new T()` (what a
        // bodyless verb used to emit unconditionally) is CS7036. This is one of the most natural
        // shapes in the library — e.g. `PlaceOrderCommand(string Product, int Quantity)` — not a
        // contrived one, and none of the earlier bodyless-verb fixtures used positional properties,
        // which is how this got past them.
        const string source = """
                              using System;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record GetTaskQuery(Guid TaskId) : IRequest<int>;

                              [Get("/tasks/{taskId:guid}")]
                              public sealed class GetTaskEndpoint : Endpoint<GetTaskQuery, int>;
                              """;

        // Act
        GeneratorHarness.AssertGeneratedCompiles(source);
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert — TaskId is parsed before construction and passed as a constructor argument rather
        // than through a `with` expression (which would need the object to already exist).
        Assert.Contains("new global::TestNs.GetTaskQuery(valueTaskId)", generated);
        Assert.DoesNotContain("with { TaskId", generated);
    }

    [Fact]
    public void Generate_ForPositionalRecordWithExtraSettableProperty_BindsBothTheCtorArgAndTheProperty()
    {
        // Arrange — a shape with both a positional constructor parameter (Id) and a plain settable
        // property declared separately in the body (Extra, not part of the constructor at all).
        // Id must be supplied to the constructor; Extra must still be bound afterwards, normally.
        const string source = """
                              using System;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record MixedShapeQuery(Guid Id) : IRequest<int>
                              {
                                  public string? Extra { get; set; }
                              }

                              [Get("/mixedshape/{id}")]
                              public sealed class MixedShapeEndpoint : Endpoint<MixedShapeQuery, int>;
                              """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert
        Assert.Contains("new global::TestNs.MixedShapeQuery(valueId)", generated);
        Assert.Contains("if (global::UnambitiousFx.Synapse.Endpoints.Binding.BindingHelpers.TryGetQuery(context, \"Extra\", out var rawExtra))",
            generated);
        Assert.Contains("message.Extra = rawExtra;", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    [Fact]
    public void Generate_ForPositionalRecordParameterWithNoMatchingProperty_UsesDefault()
    {
        // Arrange — a hand-written constructor parameter that does not correspond to any bindable
        // property at all (not merely one excluded for having no TryParse): nothing can supply it,
        // so `default` is the honest, still-compiling answer (Task 17's SYNE012 territory if the
        // property existed but had no parse path; here there is no property to even consider).
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed class HandWrittenQuery : IRequest<int>
                              {
                                  public HandWrittenQuery(int computedOnly)
                                  {
                                      ComputedOnly = computedOnly;
                                  }

                                  public int ComputedOnly { get; }
                              }

                              [Get("/handwritten")]
                              public sealed class HandWrittenEndpoint : Endpoint<HandWrittenQuery, int>;
                              """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert
        Assert.Contains("new global::TestNs.HandWrittenQuery(default)", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    [Fact]
    public void Generate_ForPositionalConstructorParameterWithNoMatchingProperty_UsesNullForgivingDefaultForReferenceType()
    {
        // Arrange — same shape as the value-type case above ("label" has no corresponding property
        // at all), but with a reference-typed constructor parameter. Emitting bare `default` here
        // would compile (Task 15's original behaviour) but raises a nullable-reference *warning* on
        // the generated code — invisible to AssertGeneratedCompiles, which only fails on Error-severity
        // diagnostics, so a consumer building with warnings-as-errors would fail on code our own test
        // suite never caught. `default!` suppresses that warning at the source (Task 17).
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed class HandWrittenQuery : IRequest<int>
                              {
                                  public HandWrittenQuery(string label)
                                  {
                                  }

                                  public int Id { get; init; }
                              }

                              [Get("/handwritten")]
                              public sealed class HandWrittenEndpoint : Endpoint<HandWrittenQuery, int>;
                              """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert — the emitted text itself is the assertion that matters here: a compile check alone
        // would not catch a regression back to bare `default`, since that still compiles cleanly.
        Assert.Contains("new global::TestNs.HandWrittenQuery(default!)", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    [Fact]
    public void Generate_ForTypeSharedByEndpointsWithDifferentVerbs_KnownLimitationUsesFirstEndpointsResolution()
    {
        // Arrange — SharedCommand is bound by two endpoints with different verbs and routes.
        // EndpointRegistry.RegisterBinder<TRequest> is keyed by request TYPE, so only one binder can
        // exist for SharedCommand; today it is built from whichever endpoint sorts first ordinally by
        // fully-qualified name ("AEndpoint" before "BEndpoint"), regardless of which endpoint actually
        // receives a given request at runtime. This is a known, defined limitation (see
        // EndpointTarget.BoundProperties' remarks) that SYNE013 (Task 17) now reports as a warning
        // rather than leaving silent — the emitted behaviour itself is unchanged (still whichever
        // endpoint sorts first), so this test still pins that part, and BindingDiagnosticTests covers
        // the diagnostic itself in more detail.
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
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert — AEndpoint (GET, route "value") wins: Value binds from the route rather than the
        // body, and the body is never read at all, as if BEndpoint (POST) did not exist.
        Assert.Contains("TryGetRoute(context, \"value\", out var", generated);
        Assert.DoesNotContain("ReadJsonBodyAsync", generated);

        // Only one binder class is emitted for the shared type.
        var occurrences = generated.Split("IEndpointBinder<global::TestNs.SharedCommand>").Length - 1;
        Assert.Equal(1, occurrences);

        GeneratorHarness.AssertGeneratedCompiles(source);

        // And the conflict is no longer silent: SYNE013 names both the type and both endpoints.
        var diagnostics = GeneratorHarness.GetDiagnostics(source);
        var syne013 = Assert.Single(diagnostics, d => d.Id == "SYNE013").GetMessage();
        Assert.Contains("SharedCommand", syne013);
        Assert.Contains("AEndpoint", syne013);
        Assert.Contains("BEndpoint", syne013);
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

    // A whole-branch review found this shape broken end to end: with no route attribute the
    // generator has no verb string, concluded "not bodyless", and emitted a ReadJsonBodyAsync call
    // into the binder — so a GET declared in Configure 500'd on every request with
    // "content type '' is not a known JSON content type". No generator test covered a
    // no-attribute endpoint at all, which is what let it ship. An empty verb is now bodyless.
    [Fact]
    public void Generate_ForRouteDeclaredInConfigure_EmitsNoBodyReadAndBindsFromQuery()
    {
        // Arrange — the documented "computed route" escape hatch: no route attribute at all, the
        // route (and therefore the verb) declared inside Configure.
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;
                              using UnambitiousFx.Synapse.Endpoints.Builders;

                              namespace TestNs;

                              public sealed record ComputedQuery : IRequest<int>
                              {
                                  public string? Filter { get; init; }
                              }

                              public sealed class ComputedEndpoint : Endpoint<ComputedQuery, int>
                              {
                                  public override void Configure(IEndpointBuilder<int> builder)
                                  {
                                      builder.Get("/computed/" + System.Environment.MachineName);
                                  }
                              }
                              """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert — no body read, and the unannotated property resolved to the query string.
        Assert.DoesNotContain("ReadJsonBodyAsync", generated);
        Assert.Contains("TryGetQuery(context, \"Filter\", out var", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    // SYNE008 is the generator saying out loud which types it believes reach the JSON deserializer.
    // Before the fix it demanded the *request* type of a Configure-declared GET be JSON-registered,
    // which is how the review independently confirmed the generator thought that GET read a body.
    [Fact]
    public void Generate_ForRouteDeclaredInConfigure_DoesNotRequireTheRequestTypeToBeJsonRegistered()
    {
        // Arrange — a JsonSerializerContext exists (so SYNE008's gate is open) and registers the
        // response type but not the request type.
        const string source = """
                              using System.Text.Json.Serialization;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;
                              using UnambitiousFx.Synapse.Endpoints.Builders;

                              namespace TestNs;

                              public sealed record ComputedQuery : IRequest<ThingDto>
                              {
                                  public string? Filter { get; init; }
                              }

                              public sealed record ThingDto(int Id);

                              public sealed class ComputedEndpoint : Endpoint<ComputedQuery, ThingDto>
                              {
                                  public override void Configure(IEndpointBuilder<ThingDto> builder)
                                  {
                                      builder.Get("/computed/" + System.Environment.MachineName);
                                  }
                              }

                              [JsonSerializable(typeof(ThingDto))]
                              internal sealed partial class AppJsonContext : JsonSerializerContext;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.DoesNotContain(diagnostics, d => d.Id == "SYNE008");
    }

    // docs/docs/endpoints.mdx points at [HttpEndpoint("OPTIONS", ...)] as the way to declare the
    // verbs with no dedicated attribute, and neither OPTIONS nor TRACE carries a request body per
    // RFC 9110 — but both used to fall into the "reads a body" branch. HEAD is included as the
    // control: it was already in the bodyless set, so a regression there would show up here too.
    [Theory]
    [InlineData("GET")]
    [InlineData("DELETE")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    [InlineData("TRACE")]
    public void Generate_ForABodylessVerb_EmitsNoBodyRead(string verb)
    {
        // Arrange
        var source = $$"""
                       using UnambitiousFx.Synapse.Abstractions;
                       using UnambitiousFx.Synapse.Endpoints;

                       namespace TestNs;

                       public sealed record ProbeQuery : IRequest<int>
                       {
                           public string? Filter { get; init; }
                       }

                       [HttpEndpoint("{{verb}}", "/probe")]
                       public sealed class ProbeEndpoint : Endpoint<ProbeQuery, int>;
                       """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert
        Assert.DoesNotContain("ReadJsonBodyAsync", generated);
        Assert.Contains("TryGetQuery(context, \"Filter\", out var", generated);
    }

    // The other half of the same claim: the verbs that do carry a body must keep reading one, so
    // widening the bodyless set cannot be mistaken for "nothing reads a body any more".
    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    public void Generate_ForABodyCarryingVerb_StillEmitsABodyRead(string verb)
    {
        // Arrange
        var source = $$"""
                       using UnambitiousFx.Synapse.Abstractions;
                       using UnambitiousFx.Synapse.Endpoints;

                       namespace TestNs;

                       public sealed record SubmitCommand : IRequest<int>
                       {
                           public string? Filter { get; init; }
                       }

                       [HttpEndpoint("{{verb}}", "/submit")]
                       public sealed class SubmitEndpoint : Endpoint<SubmitCommand, int>;
                       """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert
        Assert.Contains("ReadJsonBodyAsync<global::TestNs.SubmitCommand>(context)", generated);
    }
}
