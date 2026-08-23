namespace UnambitiousFx.Synapse.Endpoints.Generator.Tests;

/// <summary>
///     Covers the three advisory diagnostics reported by <c>EndpointsGenerator</c>: SYNE003 (a
///     POST/PUT endpoint returning a value with no explicit success mapping), SYNE004 (an
///     <c>OnSuccess</c> override that conflicts with a declarative success method), and SYNE008 (a
///     request/response type missing from every <c>JsonSerializerContext</c> in the compilation).
///     Each diagnostic gets a test that it fires and a test that it stays silent on the equivalent
///     correct shape.
/// </summary>
public sealed class AdvisoryDiagnosticTests
{
    [Fact]
    public void Generate_WhenPostEndpointDeclaresNoSuccessMapping_ReportsSyne003()
    {
        // Arrange
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record CreateThingCommand : IRequest<int>;

                              [Post("/things")]
                              public sealed class CreateThingEndpoint : Endpoint<CreateThingCommand, int>;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.Contains(diagnostics, d => d.Id == "SYNE003");
    }

    [Fact]
    public void Generate_WhenPostEndpointCallsCreatedInConfigure_ReportsNoSyne003()
    {
        // Arrange — an explicit declarative success mapping satisfies SYNE003.
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;
                              using UnambitiousFx.Synapse.Endpoints.Builders;

                              namespace TestNs;

                              public sealed record CreateThingCommand : IRequest<int>;

                              [Post("/things")]
                              public sealed class CreateThingEndpoint : Endpoint<CreateThingCommand, int>
                              {
                                  public override void Configure(IEndpointBuilder<int> builder)
                                  {
                                      builder.Created(id => $"/things/{id}");
                                  }
                              }
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.DoesNotContain(diagnostics, d => d.Id == "SYNE003");
    }

    [Fact]
    public void Generate_WhenGetEndpointDeclaresNoSuccessMapping_ReportsNoSyne003()
    {
        // Arrange — SYNE003 only concerns POST/PUT; a GET returning a value with no explicit
        // mapping is unremarkable (200 OK is the conventional response for GET).
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record GetThingQuery : IRequest<int>;

                              [Get("/things")]
                              public sealed class GetThingEndpoint : Endpoint<GetThingQuery, int>;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.DoesNotContain(diagnostics, d => d.Id == "SYNE003");
    }

    [Fact]
    public void Generate_WhenEndpointOverridesOnSuccessAndConfigureCallsCreated_ReportsSyne004()
    {
        // Arrange — the declarative Created(...) call always wins over the OnSuccess override at
        // dispatch time, so the override is dead code.
        const string source = """
                              using Microsoft.AspNetCore.Http;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;
                              using UnambitiousFx.Synapse.Endpoints.Builders;

                              namespace TestNs;

                              public sealed record CreateThingCommand : IRequest<int>;

                              [Post("/things")]
                              public sealed class CreateThingEndpoint : Endpoint<CreateThingCommand, int>
                              {
                                  public override void Configure(IEndpointBuilder<int> builder)
                                  {
                                      builder.Created(id => $"/things/{id}");
                                  }

                                  public override IResult OnSuccess(int response, HttpContext context)
                                  {
                                      return TypedResults.Ok(response);
                                  }
                              }
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.Contains(diagnostics, d => d.Id == "SYNE004");
    }

    [Fact]
    public void Generate_WhenEndpointOnlyOverridesOnSuccess_ReportsNoSyne004()
    {
        // Arrange — no declarative call in Configure, so there is nothing for the override to
        // conflict with.
        const string source = """
                              using Microsoft.AspNetCore.Http;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record CreateThingCommand : IRequest<int>;

                              [Post("/things")]
                              public sealed class CreateThingEndpoint : Endpoint<CreateThingCommand, int>
                              {
                                  public override IResult OnSuccess(int response, HttpContext context)
                                  {
                                      return TypedResults.Created($"/things/{response}", response);
                                  }
                              }
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.DoesNotContain(diagnostics, d => d.Id == "SYNE004");
    }

    [Fact]
    public void Generate_WhenConfigureOnlyCallsCreatedWithNoOnSuccessOverride_ReportsNoSyne004()
    {
        // Arrange — the recommended shape: only the declarative call, no override to conflict
        // with it.
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;
                              using UnambitiousFx.Synapse.Endpoints.Builders;

                              namespace TestNs;

                              public sealed record CreateThingCommand : IRequest<int>;

                              [Post("/things")]
                              public sealed class CreateThingEndpoint : Endpoint<CreateThingCommand, int>
                              {
                                  public override void Configure(IEndpointBuilder<int> builder)
                                  {
                                      builder.Created(id => $"/things/{id}");
                                  }
                              }
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.DoesNotContain(diagnostics, d => d.Id == "SYNE004");
    }

    [Fact]
    public void Generate_WhenResponseTypeIsMissingFromTheJsonContext_ReportsSyne008()
    {
        // Arrange
        const string source = """
                              using System.Text.Json.Serialization;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record ThingDto(string Name);
                              public sealed record GetThingQuery : IRequest<ThingDto>;

                              [Get("/things")]
                              public sealed class GetThingEndpoint : Endpoint<GetThingQuery, ThingDto>;

                              [JsonSerializable(typeof(GetThingQuery))]
                              internal sealed partial class AppJsonContext : JsonSerializerContext;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        var diagnostic = Assert.Single(diagnostics, d => d.Id == "SYNE008");
        Assert.Contains("ThingDto", diagnostic.GetMessage());
    }

    [Fact]
    public void Generate_WhenNoJsonContextExists_DoesNotReportSyne008()
    {
        // Arrange
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record ThingDto(string Name);
                              public sealed record GetThingQuery : IRequest<ThingDto>;

                              [Get("/things")]
                              public sealed class GetThingEndpoint : Endpoint<GetThingQuery, ThingDto>;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.DoesNotContain(diagnostics, d => d.Id == "SYNE008");
    }

    [Fact]
    public void Generate_WhenRequestAndResponseTypesAreBothRegistered_DoesNotReportSyne008()
    {
        // Arrange
        const string source = """
                              using System.Text.Json.Serialization;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record ThingDto(string Name);
                              public sealed record GetThingQuery : IRequest<ThingDto>;

                              [Get("/things")]
                              public sealed class GetThingEndpoint : Endpoint<GetThingQuery, ThingDto>;

                              [JsonSerializable(typeof(GetThingQuery))]
                              [JsonSerializable(typeof(ThingDto))]
                              internal sealed partial class AppJsonContext : JsonSerializerContext;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.DoesNotContain(diagnostics, d => d.Id == "SYNE008");
    }

    [Fact]
    public void Generate_WhenResponseTypeIsPrimitive_DoesNotReportSyne008()
    {
        // Arrange — int needs no registration: the source-generated resolver supports it
        // intrinsically, so it must never be reported even though it is not in the context.
        const string source = """
                              using System.Text.Json.Serialization;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record GetThingQuery : IRequest<int>;

                              [Get("/things")]
                              public sealed class GetThingEndpoint : Endpoint<GetThingQuery, int>;

                              [JsonSerializable(typeof(GetThingQuery))]
                              internal sealed partial class AppJsonContext : JsonSerializerContext;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.DoesNotContain(diagnostics, d => d.Id == "SYNE008");
    }

    [Fact]
    public void Generate_WhenBodylessRequestTypeIsUnregistered_DoesNotReportSyne008()
    {
        // Arrange — GetThingQuery is bound entirely from the query string on this bodyless GET, so
        // it never reaches the JSON deserializer and must not be reported even though it is absent
        // from the context. Only the response type (ThingDto) is registered.
        const string source = """
                              using System.Text.Json.Serialization;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record ThingDto(string Name);
                              public sealed record GetThingQuery : IRequest<ThingDto>
                              {
                                  public string? Search { get; init; }
                              }

                              [Get("/things")]
                              public sealed class GetThingEndpoint : Endpoint<GetThingQuery, ThingDto>;

                              [JsonSerializable(typeof(ThingDto))]
                              internal sealed partial class AppJsonContext : JsonSerializerContext;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.DoesNotContain(diagnostics, d => d.Id == "SYNE008");
    }

    [Fact]
    public void Generate_WhenPostRequestTypeIsUnregistered_ReportsSyne008()
    {
        // Arrange — POST is not a bodyless verb, so CreateThingCommand is deserialized from the
        // JSON body and must be registered.
        const string source = """
                              using System.Text.Json.Serialization;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record CreateThingCommand(string Name) : IRequest<int>;

                              [Post("/things")]
                              public sealed class CreateThingEndpoint : Endpoint<CreateThingCommand, int>;

                              [JsonSerializable(typeof(int))]
                              internal sealed partial class AppJsonContext : JsonSerializerContext;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        var diagnostic = Assert.Single(diagnostics, d => d.Id == "SYNE008");
        Assert.Contains("CreateThingCommand", diagnostic.GetMessage());
    }

    [Fact]
    public void Generate_WhenResponseCollectionTypeIsRegisteredAsItsExactClosedType_DoesNotReportSyne008()
    {
        // Arrange — the collection is registered as the exact closed generic type used by the
        // endpoint (IReadOnlyList<ThingDto>), which is what the response invoker actually
        // serializes; the element type (ThingDto) is not separately registered, and that must be
        // sufficient — registering only the element type would not, by itself, make the collection
        // type serializable, so the check is deliberately against the exact declared type.
        const string source = """
                              using System.Collections.Generic;
                              using System.Text.Json.Serialization;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record ThingDto(string Name);
                              public sealed record ListThingsQuery : IRequest<IReadOnlyList<ThingDto>>;

                              [Get("/things")]
                              public sealed class ListThingsEndpoint : Endpoint<ListThingsQuery, IReadOnlyList<ThingDto>>;

                              [JsonSerializable(typeof(IReadOnlyList<ThingDto>))]
                              internal sealed partial class AppJsonContext : JsonSerializerContext;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.DoesNotContain(diagnostics, d => d.Id == "SYNE008");
    }

    [Fact]
    public void Generate_WhenOnlyTheCollectionElementTypeIsRegistered_ReportsSyne008ForTheCollectionType()
    {
        // Arrange — the inverse of the previous case: registering only the element type (ThingDto)
        // does not cover the collection type (IReadOnlyList<ThingDto>) actually used as the
        // response, so the collection type itself must be reported as missing.
        const string source = """
                              using System.Collections.Generic;
                              using System.Text.Json.Serialization;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record ThingDto(string Name);
                              public sealed record ListThingsQuery : IRequest<IReadOnlyList<ThingDto>>;

                              [Get("/things")]
                              public sealed class ListThingsEndpoint : Endpoint<ListThingsQuery, IReadOnlyList<ThingDto>>;

                              [JsonSerializable(typeof(ThingDto))]
                              internal sealed partial class AppJsonContext : JsonSerializerContext;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        var diagnostic = Assert.Single(diagnostics, d => d.Id == "SYNE008");
        Assert.Contains("IReadOnlyList<TestNs.ThingDto>", diagnostic.GetMessage());
    }

    [Fact]
    public void Generate_WhenJsonContextIsDefinedInAReferencedAssembly_DoesNotReportSyne008()
    {
        // Arrange — the JsonSerializerContext lives in a separately compiled assembly, exercising
        // the reference-graph scan (rather than the current compilation's own declarations). A
        // consumer commonly defines shared JSON contracts and their context in one project,
        // referenced from several endpoint projects.
        const string contextSource = """
                                      using System.Text.Json;
                                      using System.Text.Json.Serialization;
                                      using System.Text.Json.Serialization.Metadata;

                                      namespace ContextLib;

                                      public sealed record ThingDto(string Name);

                                      [JsonSerializable(typeof(ThingDto))]
                                      internal sealed class AppJsonContext : JsonSerializerContext
                                      {
                                          public AppJsonContext() : base(null)
                                          {
                                          }

                                          protected override JsonSerializerOptions? GeneratedSerializerOptions => null;

                                          public override JsonTypeInfo? GetTypeInfo(System.Type type) => null;
                                      }
                                      """;

        var contextReference = GeneratorHarness.CompileToReference(contextSource, "ContextLib");

        const string source = """
                              using ContextLib;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record GetThingQuery : IRequest<ThingDto>;

                              [Get("/things")]
                              public sealed class GetThingEndpoint : Endpoint<GetThingQuery, ThingDto>;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source, contextReference);

        // Assert
        Assert.DoesNotContain(diagnostics, d => d.Id == "SYNE008");
    }
}
