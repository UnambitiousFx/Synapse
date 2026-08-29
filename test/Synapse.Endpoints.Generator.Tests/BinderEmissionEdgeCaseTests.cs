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
        // The raw string lands in a local first: every value is read and its problems collected
        // before anything is assigned, so one request reports all of its bad values at once.
        Assert.Contains("valueName = rawName!;", generated);
        Assert.Contains("message.Name = valueName;", generated);
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
        // Assigned into a pre-declared local rather than `out var`, so the value survives the
        // accumulation branches; enums are not culture-sensitive, so no format provider is passed.
        Assert.Contains("global::System.Enum.TryParse<global::System.DayOfWeek>(rawDay, out valueDay)", generated);
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
        Assert.Contains("message.Extra = valueExtra;", generated);
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
        Assert.Contains(
            "int.TryParse(rawId, global::System.Globalization.CultureInfo.InvariantCulture, out valueId)",
            generated);
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

    // docs/docs/endpoints/reference/base-classes.mdx points at [HttpEndpoint("OPTIONS", ...)] as the way to declare the
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

/// <summary>
///     Covers the message shapes a review found the binder mishandled: a type implementing
///     <c>IParsable&lt;T&gt;</c> the canonical way, a <c>required</c> member no constructor parameter
///     covers, a constructor parameter whose type differs from the property that shares its name, a
///     constructor parameter with a default, and an <c>internal</c> constructor in a referenced
///     assembly. See docs/known-issues/057 through 061.
/// </summary>
public sealed class BinderConstructionShapeTests
{
    // A strongly-typed id written the modern way implements IParsable<T>, which mandates only
    // TryParse(string?, IFormatProvider?, out T). The emitter has emitted that overload since the
    // invariant-culture fix, but the bindability gate still demanded the two-argument form, so the
    // property was omitted (SYNE012), the route parameter then matched nothing (SYNE001), and the
    // blocking error suppressed the endpoint entirely. See docs/known-issues/057.
    private const string ParsableOnlySource = """
                                              using System;
                                              using System.Diagnostics.CodeAnalysis;
                                              using UnambitiousFx.Synapse.Abstractions;
                                              using UnambitiousFx.Synapse.Endpoints;

                                              namespace TestNs;

                                              public readonly record struct TaskId(Guid Value) : IParsable<TaskId>
                                              {
                                                  public static TaskId Parse(string s, IFormatProvider? provider) => new(Guid.Parse(s));

                                                  public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out TaskId result)
                                                  {
                                                      if (Guid.TryParse(s, out var value)) { result = new TaskId(value); return true; }
                                                      result = default;
                                                      return false;
                                                  }
                                              }

                                              public sealed record GetTask(TaskId TaskId) : IRequest;

                                              [Get("/tasks/{taskId}")]
                                              public sealed class GetTaskEndpoint : Endpoint<GetTask>;
                                              """;

    [Fact]
    public void Generate_ForATypeImplementingOnlyIParsable_BindsItThroughTheInvariantCultureOverload()
    {
        // Act
        var generated = GeneratorHarness.GetFile(ParsableOnlySource, "SynapseEndpointBinders.g.cs");

        // Assert
        Assert.Contains(
            "global::TestNs.TaskId.TryParse(rawTaskId, global::System.Globalization.CultureInfo.InvariantCulture, out valueTaskId)",
            generated);
        GeneratorHarness.AssertGeneratedCompiles(ParsableOnlySource);
    }

    // The cascade is the damage: one omitted property took the whole endpoint with it, so the route
    // was never mapped at all.
    [Fact]
    public void Generate_ForATypeImplementingOnlyIParsable_ReportsNothing()
    {
        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(ParsableOnlySource);

        // Assert
        Assert.DoesNotContain(diagnostics, d => d.Id is "SYNE012" or "SYNE001");
    }

    // required is enforced at the creation site, so `new T(id)` followed by an assignment is CS9035
    // however the value is applied afterwards. It has to be set in the object initializer.
    [Fact]
    public void Generate_ForARequiredPropertyNoConstructorParameterCovers_SetsItInTheObjectInitializer()
    {
        // Arrange
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record GetThing(int Id) : IRequest
                              {
                                  public required string Tenant { get; init; }
                              }

                              [Get("/things/{id}")]
                              public sealed class GetThingEndpoint : Endpoint<GetThing>;
                              """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert — set at construction, and not also assigned afterwards.
        Assert.Contains("new global::TestNs.GetThing(valueId) { Tenant = valueTenant };", generated);
        Assert.DoesNotContain("message with { Tenant", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    // The same fix retires a documented limitation: `required` on a message with a parameterless
    // constructor used to be impossible, because `new T()` followed by a `with` expression cannot
    // satisfy a required member. An object initializer can.
    [Fact]
    public void Generate_ForARequiredPropertyOnAParameterlessConstructor_SetsItInTheObjectInitializer()
    {
        // Arrange
        const string source = """
                              using System;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record GetTaskQuery : IRequest
                              {
                                  public required Guid TaskId { get; init; }
                              }

                              [Get("/tasks/{taskId}")]
                              public sealed class GetTaskEndpoint : Endpoint<GetTaskQuery>;
                              """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert
        Assert.Contains("new global::TestNs.GetTaskQuery() { TaskId = valueTaskId };", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    // A name match is not enough to pass a value to a parameter. int? does not convert to int
    // (CS1503), and string? into a non-nullable string parameter warns (CS8604), which fails a
    // TreatWarningsAsErrors build on code the consumer cannot edit.
    [Theory]
    [InlineData("int? Page { get; set; }", "int page", "Page")]
    [InlineData("string? Name { get; set; }", "string name", "Name")]
    public void Generate_ForAConstructorParameterOfADifferentType_DoesNotPassThePropertyToIt(
        string property,
        string parameter,
        string propertyName)
    {
        // Arrange
        var source = $$"""
                       using UnambitiousFx.Synapse.Abstractions;
                       using UnambitiousFx.Synapse.Endpoints;

                       namespace TestNs;

                       public sealed class Query : IRequest
                       {
                           public Query({{parameter}}) { {{propertyName}} = {{propertyName.ToLowerInvariant()}}; }
                           public {{property}}
                       }

                       [Get("/queries")]
                       public sealed class QueryEndpoint : Endpoint<Query>;
                       """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert — the parameter falls back to a default and the value is applied afterwards instead.
        Assert.DoesNotContain($"new global::TestNs.Query(value{propertyName})", generated);
        Assert.Contains($"message.{propertyName} = value{propertyName};", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    // A constructor default says what the type wants when nothing is sent. Overwriting it with
    // default(T) — or rejecting the request as if the value were mandatory — discards that.
    [Fact]
    public void Generate_ForConstructorParametersWithDefaults_FallsBackToThemInsteadOfRequiringAValue()
    {
        // Arrange
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record ListUsers(int Page = 1, string? Sort = "name") : IRequest;

                              [Get("/users")]
                              public sealed class ListUsersEndpoint : Endpoint<ListUsers>;
                              """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert — each local starts at the declared default, and an absent value is not an error.
        Assert.Contains("int valuePage = (int)(1);", generated);
        Assert.Contains("string? valueSort = (string)(\"name\");", generated);
        Assert.DoesNotContain("The query value is required.", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    // Microsoft.AspNetCore.Mvc's FromHeader must bind a header, not fall through to the convention.
    // A message file typically imports MVC (for [FromQuery]/[FromRoute]) and not
    // UnambitiousFx.Synapse.Endpoints — the endpoint class lives elsewhere — so [FromHeader] resolves
    // to MVC's with no ambiguity, and being ignored meant reading the query string under the property
    // name instead. See docs/known-issues/062.
    [Fact]
    public void Generate_ForTheMvcFromHeaderAttribute_ReadsTheHeaderAndNotTheQueryString()
    {
        // Arrange — exactly the using set of a message file that does not declare its own endpoint.
        const string source = """
                              namespace TestNs
                              {
                                  using Microsoft.AspNetCore.Mvc;
                                  using UnambitiousFx.Synapse.Abstractions;

                                  public sealed record Q : IRequest
                                  {
                                      [FromHeader(Name = "If-Match")] public string? IfMatch { get; init; }
                                  }
                              }

                              namespace TestNs2
                              {
                                  using UnambitiousFx.Synapse.Endpoints;

                                  [Get("/q")]
                                  public sealed class QEndpoint : Endpoint<TestNs.Q>;
                              }
                              """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert — the declared header name, through the header reader.
        Assert.Contains("TryGetHeader(context, \"If-Match\"", generated);
        Assert.DoesNotContain("TryGetQuery(context, \"IfMatch\"", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    // internal is internal to the assembly that declares it. A message type from a referenced
    // contracts assembly whose only parameterless constructor is internal cannot be constructed by
    // the generated binder, and calling it anyway is CS1729.
    [Fact]
    public void Generate_ForAnInternalConstructorInAReferencedAssembly_DoesNotCallIt()
    {
        // Arrange
        var contracts = GeneratorHarness.CompileToReference(
            """
            using UnambitiousFx.Synapse.Abstractions;

            namespace Contracts;

            public sealed class ExternalQuery : IRequest
            {
                internal ExternalQuery() { }
                public ExternalQuery(string name) { Name = name; }
                public string? Name { get; set; }
            }
            """,
            "Contracts");

        const string source = """
                              using Contracts;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              [Get("/externals")]
                              public sealed class ExternalEndpoint : Endpoint<ExternalQuery>;
                              """;

        // Act
        var generated = GeneratorHarness.GetFileWithReferences(source, "SynapseEndpointBinders.g.cs", contracts);

        // Assert — the accessible (public) constructor is used instead of the internal one.
        Assert.DoesNotContain("new global::Contracts.ExternalQuery()", generated);
        GeneratorHarness.AssertGeneratedCompilesWithReferences(source, contracts);
    }
}
