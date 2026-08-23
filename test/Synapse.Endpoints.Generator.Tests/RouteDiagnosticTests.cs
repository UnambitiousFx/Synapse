namespace UnambitiousFx.Synapse.Endpoints.Generator.Tests;

/// <summary>
///     Covers the five route/endpoint-shape diagnostics reported by <c>EndpointsGenerator</c>:
///     SYNE001 (route parameter has no matching property), SYNE005 (stream message on a non-stream
///     endpoint), SYNE006 (<c>[InGroup&lt;T&gt;]</c> where <c>T</c> is not an <c>EndpointGroup</c>),
///     SYNE009 (route declared both by attribute and in <c>Configure</c>) and SYNE010 (an endpoint
///     shape <c>MapEndpoint&lt;TEndpoint&gt;()</c> cannot be instantiated for). Each diagnostic gets a
///     test that it fires and a test that it stays silent on the equivalent correct shape.
/// </summary>
public sealed class RouteDiagnosticTests
{
    [Fact]
    public void Generate_WhenRouteParameterHasNoProperty_ReportsSyne001()
    {
        // Arrange
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record GetThingQuery : IRequest<int>;

                              [Get("/things/{thingId}")]
                              public sealed class GetThingEndpoint : Endpoint<GetThingQuery, int>;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.Contains(diagnostics, d => d.Id == "SYNE001");
        Assert.Contains("thingId", Assert.Single(diagnostics, d => d.Id == "SYNE001").GetMessage());
    }

    [Fact]
    public void Generate_WhenRouteParameterHasMatchingProperty_ReportsNoSyne001()
    {
        // Arrange
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record GetThingQuery : IRequest<int>
                              {
                                  public int ThingId { get; init; }
                              }

                              [Get("/things/{thingId}")]
                              public sealed class GetThingEndpoint : Endpoint<GetThingQuery, int>;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.DoesNotContain(diagnostics, d => d.Id == "SYNE001");
    }

    [Fact]
    public void Generate_WhenStreamMessageUsesNonStreamEndpoint_ReportsSyne005()
    {
        // Arrange — TickQuery is dispatchable through IRequest<int> (satisfying Endpoint<,>'s
        // constraint) but also implements IStreamRequest<int>, which almost certainly means the
        // endpoint meant to derive from StreamEndpoint<,> instead.
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record TickQuery : IRequest<int>, IStreamRequest<int>;

                              [Get("/ticks")]
                              public sealed class TickEndpoint : Endpoint<TickQuery, int>;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.Contains(diagnostics, d => d.Id == "SYNE005");
    }

    [Fact]
    public void Generate_WhenStreamMessageUsesStreamEndpoint_ReportsNoSyne005()
    {
        // Arrange
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record TickQuery : IStreamRequest<int>;

                              [Get("/ticks")]
                              public sealed class TickEndpoint : StreamEndpoint<TickQuery, int>;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.DoesNotContain(diagnostics, d => d.Id == "SYNE005");
    }

    [Fact]
    public void Generate_WhenInGroupTypeIsNotAnEndpointGroup_ReportsSyne006()
    {
        // Arrange
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed class NotAGroup
                              {
                              }

                              public sealed record GetThingQuery : IRequest<int>
                              {
                                  public int ThingId { get; init; }
                              }

                              [Get("/things/{thingId}")]
                              [InGroup<NotAGroup>]
                              public sealed class GetThingEndpoint : Endpoint<GetThingQuery, int>;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.Contains(diagnostics, d => d.Id == "SYNE006");
    }

    [Fact]
    public void Generate_WhenInGroupTypeDerivesFromEndpointGroup_ReportsNoSyne006()
    {
        // Arrange
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;
                              using UnambitiousFx.Synapse.Endpoints.Builders;

                              namespace TestNs;

                              public sealed class MyGroup : EndpointGroup
                              {
                                  public override void Configure(IEndpointGroupBuilder builder)
                                  {
                                  }
                              }

                              public sealed record GetThingQuery : IRequest<int>
                              {
                                  public int ThingId { get; init; }
                              }

                              [Get("/things/{thingId}")]
                              [InGroup<MyGroup>]
                              public sealed class GetThingEndpoint : Endpoint<GetThingQuery, int>;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.DoesNotContain(diagnostics, d => d.Id == "SYNE006");
    }

    [Fact]
    public void Generate_WhenRouteAttributeAndConfigureBothDeclareARoute_ReportsSyne009()
    {
        // Arrange
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;
                              using UnambitiousFx.Synapse.Endpoints.Builders;

                              namespace TestNs;

                              public sealed record GetThingQuery : IRequest<int>
                              {
                                  public int ThingId { get; init; }
                              }

                              [Get("/things/{thingId}")]
                              public sealed class GetThingEndpoint : Endpoint<GetThingQuery, int>
                              {
                                  public override void Configure(IEndpointBuilder<int> builder)
                                  {
                                      builder.Get("/things/{thingId}");
                                  }
                              }
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.Contains(diagnostics, d => d.Id == "SYNE009");
    }

    [Fact]
    public void Generate_WhenConfigureOnlySetsMetadata_ReportsNoSyne009()
    {
        // Arrange — Configure is overridden (unlike the base "correct endpoint" fixture) but never
        // calls a verb/route method, so the attribute remains the sole route declaration.
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;
                              using UnambitiousFx.Synapse.Endpoints.Builders;

                              namespace TestNs;

                              public sealed record GetThingQuery : IRequest<int>
                              {
                                  public int ThingId { get; init; }
                              }

                              [Get("/things/{thingId}")]
                              public sealed class GetThingEndpoint : Endpoint<GetThingQuery, int>
                              {
                                  public override void Configure(IEndpointBuilder<int> builder)
                                  {
                                      builder.Tag("things").Summary("Gets a thing.");
                                  }
                              }
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.DoesNotContain(diagnostics, d => d.Id == "SYNE009");
    }

    [Fact]
    public void Generate_WhenEndpointClassIsGeneric_ReportsSyne010()
    {
        // Arrange
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record GetThingQuery : IRequest<int>
                              {
                                  public int ThingId { get; init; }
                              }

                              [Get("/things/{thingId}")]
                              public sealed class GetThingEndpoint<T> : Endpoint<GetThingQuery, int>;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.Contains(diagnostics, d => d.Id == "SYNE010");
    }

    [Fact]
    public void Generate_WhenEndpointClassIsNestedInAGenericType_ReportsSyne010()
    {
        // Arrange
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record GetThingQuery : IRequest<int>
                              {
                                  public int ThingId { get; init; }
                              }

                              public sealed class Outer<T>
                              {
                                  [Get("/things/{thingId}")]
                                  public sealed class GetThingEndpoint : Endpoint<GetThingQuery, int>;
                              }
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.Contains(diagnostics, d => d.Id == "SYNE010");
    }

    [Fact]
    public void Generate_WhenEndpointHasNoPublicParameterlessConstructor_ReportsSyne010()
    {
        // Arrange
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record GetThingQuery : IRequest<int>
                              {
                                  public int ThingId { get; init; }
                              }

                              [Get("/things/{thingId}")]
                              public sealed class GetThingEndpoint : Endpoint<GetThingQuery, int>
                              {
                                  public GetThingEndpoint(int seed)
                                  {
                                  }
                              }
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.Contains(diagnostics, d => d.Id == "SYNE010");
    }

    [Fact]
    public void Generate_WhenEndpointIsTopLevelNonGenericWithParameterlessConstructor_ReportsNoSyne010()
    {
        // Arrange
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record GetThingQuery : IRequest<int>
                              {
                                  public int ThingId { get; init; }
                              }

                              [Get("/things/{thingId}")]
                              public sealed class GetThingEndpoint : Endpoint<GetThingQuery, int>
                              {
                                  public GetThingEndpoint()
                                  {
                                  }
                              }
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.DoesNotContain(diagnostics, d => d.Id == "SYNE010");
    }

    [Fact]
    public void Generate_WhenEndpointHasAnyErrorDiagnostic_SkipsEmissionForThatEndpoint()
    {
        // Arrange — the shape violation (generic) should prevent SynapseEndpointGroup.g.cs from emitting a
        // MapEndpoint<T>() call for this endpoint at all, rather than cascading into a confusing
        // "TEndpoint : EndpointBase, new()" constraint error on the generated call site.
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record GetThingQuery : IRequest<int>
                              {
                                  public int ThingId { get; init; }
                              }

                              [Get("/things/{thingId}")]
                              public sealed class GetThingEndpoint<T> : Endpoint<GetThingQuery, int>;
                              """;

        // Act
        var generated = GeneratorHarness.TryGetFile(source, "SynapseEndpointGroup.g.cs");

        // Assert
        Assert.Null(generated);
    }

    [Fact]
    public void Generate_ForCorrectEndpoint_ReportsNoDiagnostics()
    {
        // Arrange — one endpoint of every shape the generator recognizes: void command, value
        // query, mapped HTTP contract and streaming query, each declared correctly (matching route
        // parameters, no group misuse, no doubled route declaration, ordinary parameterless shape).
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;
                              using UnambitiousFx.Synapse.Endpoints.Builders;

                              namespace TestNs;

                              public sealed class MyGroup : EndpointGroup
                              {
                                  public override void Configure(IEndpointGroupBuilder builder)
                                  {
                                  }
                              }

                              public sealed record GetThingQuery : IRequest<int>
                              {
                                  public int ThingId { get; init; }
                              }

                              [Get("/things/{thingId}")]
                              [InGroup<MyGroup>]
                              public sealed class GetThingEndpoint : Endpoint<GetThingQuery, int>
                              {
                                  public override void Configure(IEndpointBuilder<int> builder)
                                  {
                                      builder.Tag("things");
                                  }
                              }

                              public sealed record DeleteThingCommand : IRequest
                              {
                                  public int ThingId { get; init; }
                              }

                              [Delete("/things/{thingId}")]
                              public sealed class DeleteThingEndpoint : Endpoint<DeleteThingCommand>;

                              public sealed record TickQuery : IStreamRequest<int>
                              {
                                  public int Count { get; init; }
                              }

                              [Get("/ticks/{count}")]
                              public sealed class TickEndpoint : StreamEndpoint<TickQuery, int>;

                              public sealed record HttpThingRequest(int ThingId);
                              public sealed record CreateThingCommand(int ThingId) : IRequest<int>;
                              public sealed record HttpThingResponse(int Id);

                              [Post("/things")]
                              public sealed class CreateThingEndpoint
                                  : MappedEndpoint<HttpThingRequest, CreateThingCommand, int, HttpThingResponse>
                              {
                                  public override CreateThingCommand ToRequest(HttpThingRequest request) =>
                                      new(request.ThingId);

                                  public override HttpThingResponse ToResponse(int response) => new(response);
                              }
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.Empty(diagnostics);
    }
}
