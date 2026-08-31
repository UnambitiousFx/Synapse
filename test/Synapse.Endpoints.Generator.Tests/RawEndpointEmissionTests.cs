namespace UnambitiousFx.Synapse.Endpoints.Generator.Tests;

/// <summary>
///     Low-level endpoints are discovered, registered and mapped exactly like the high-level ones, but
///     have no generated binder — their binding is either hand-written or does not exist.
/// </summary>
public sealed class RawEndpointEmissionTests
{
    private const string FreeFormEndpoint = """
                                            using System.Threading;
                                            using System.Threading.Tasks;
                                            using Microsoft.AspNetCore.Http;
                                            using UnambitiousFx.Synapse.Endpoints;

                                            namespace TestNs;

                                            [Get("/health")]
                                            public sealed class HealthEndpoint : RawEndpoint
                                            {
                                                public override ValueTask<IResult> HandleAsync(HttpContext context, CancellationToken cancellationToken)
                                                    => ValueTask.FromResult(TypedResults.NoContent() as IResult);
                                            }
                                            """;

    [Fact]
    public void Generate_ForAFreeFormEndpoint_MapsItAlongsideTheHighLevelOnes()
    {
        // Arrange
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using Microsoft.AspNetCore.Http;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              [Get("/health")]
                              public sealed class HealthEndpoint : RawEndpoint
                              {
                                  public override ValueTask<IResult> HandleAsync(HttpContext context, CancellationToken cancellationToken)
                                      => ValueTask.FromResult(TypedResults.NoContent() as IResult);
                              }

                              public sealed record GetThingQuery : IRequest<string>;

                              [Get("/things")]
                              public sealed class GetThingEndpoint : Endpoint<GetThingQuery, string>;
                              """;

        // Act
        var group = GeneratorHarness.GetFile(source, "SynapseEndpointGroup.g.cs");

        // Assert — one list, both levels.
        Assert.Contains("endpoints.MapEndpoint<global::TestNs.HealthEndpoint>();", group);
        Assert.Contains("endpoints.MapEndpoint<global::TestNs.GetThingEndpoint>();", group);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    [Fact]
    public void Generate_ForAFreeFormEndpoint_RegistersMetadataButNoBinder()
    {
        // Act
        var registrations = GeneratorHarness.GetFile(FreeFormEndpoint, "SynapseEndpointRegistrations.g.cs");
        var binders = GeneratorHarness.GetFile(FreeFormEndpoint, "SynapseEndpointBinders.g.cs");

        // Assert — the route still has to be registered, but there is no bound type to bind.
        Assert.Contains("RegisterMetadata<global::TestNs.HealthEndpoint>", registrations);
        Assert.DoesNotContain("RegisterBinder", registrations);
        Assert.DoesNotContain("IEndpointBinder", binders);
        GeneratorHarness.AssertGeneratedCompiles(FreeFormEndpoint);
    }

    [Fact]
    public void Generate_ForAFreeFormEndpointInAGroup_EmitsTheGroupFactory()
    {
        // Arrange
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using Microsoft.AspNetCore.Http;
                              using UnambitiousFx.Synapse.Endpoints;
                              using UnambitiousFx.Synapse.Endpoints.Builders;

                              namespace TestNs;

                              public sealed class OpsGroup : EndpointGroup
                              {
                                  public override void Configure(IEndpointGroupBuilder builder) => builder.Prefix("/ops");
                              }

                              [Get("/health")]
                              [InGroup<OpsGroup>]
                              public sealed class HealthEndpoint : RawEndpoint
                              {
                                  public override ValueTask<IResult> HandleAsync(HttpContext context, CancellationToken cancellationToken)
                                      => ValueTask.FromResult(TypedResults.NoContent() as IResult);
                              }
                              """;

        // Act
        var registrations = GeneratorHarness.GetFile(source, "SynapseEndpointRegistrations.g.cs");

        // Assert
        Assert.Contains("typeof(global::TestNs.OpsGroup), static () => new global::TestNs.OpsGroup()",
            registrations);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    [Fact]
    public void Generate_ForAHandBoundEndpoint_RegistersMetadataButNoBinder()
    {
        // Arrange
        const string source = """
                              using System.Threading.Tasks;
                              using Microsoft.AspNetCore.Http;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;
                              using UnambitiousFx.Synapse.Endpoints.Binding;

                              namespace TestNs;

                              public sealed record LookupQuery(int Id) : IRequest<string>;

                              [Get("/lookup/{id}")]
                              public sealed class LookupEndpoint : RawEndpoint<LookupQuery, string>
                              {
                                  public override ValueTask<BindResult<LookupQuery>> BindAsync(HttpContext context)
                                  {
                                      var validation = context.Validate();
                                      validation.Route<int>("id", out var id);
                                      return ValueTask.FromResult(validation.IsValid
                                          ? BindResult<LookupQuery>.Success(new LookupQuery(id))
                                          : BindResult<LookupQuery>.Failure(validation));
                                  }
                              }
                              """;

        // Act
        var registrations = GeneratorHarness.GetFile(source, "SynapseEndpointRegistrations.g.cs");

        // Assert — the endpoint supplies its own BindAsync, so generating one would be dead code that
        // could not even be registered (RegisterBinder is keyed by message type).
        Assert.Contains("RegisterMetadata<global::TestNs.LookupEndpoint>", registrations);
        Assert.DoesNotContain("RegisterBinder", registrations);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    // Discovery walks the base chain outwards from the class, so the nearest match wins. Endpoint<T,R>
    // now derives from RawEndpoint<T,R>, which derives from RawEndpoint — three candidate matches in
    // one chain. Pinned rather than trusted: if proximity ever stopped deciding, a high-level endpoint
    // would silently lose its generated binder.
    [Fact]
    public void Generate_ForAHighLevelEndpoint_StillEmitsABinderDespiteTheRawBasesInItsChain()
    {
        // Arrange
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record GetThingQuery : IRequest<string>
                              {
                                  public int Id { get; init; }
                              }

                              [Get("/things/{id}")]
                              public sealed class GetThingEndpoint : Endpoint<GetThingQuery, string>;
                              """;

        // Act
        var registrations = GeneratorHarness.GetFile(source, "SynapseEndpointRegistrations.g.cs");
        var binders = GeneratorHarness.GetFile(source, "SynapseEndpointBinders.g.cs");

        // Assert
        Assert.Contains("RegisterBinder(new TestNs_GetThingQueryBinder())", registrations);
        Assert.Contains("IEndpointBinder<global::TestNs.GetThingQuery>", binders);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    // SYNE001 exists to catch a route parameter no property can receive. A free-form endpoint has no
    // properties by design — it reads the route itself — so the rule must not fire for one.
    [Fact]
    public void Generate_ForAFreeFormEndpointWithRouteParameters_ReportsNoBindingDiagnostics()
    {
        // Arrange
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using Microsoft.AspNetCore.Http;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              [Post("/webhooks/{tenant}/{kind}")]
                              public sealed class WebhookEndpoint : RawEndpoint
                              {
                                  public override ValueTask<IResult> HandleAsync(HttpContext context, CancellationToken cancellationToken)
                                      => ValueTask.FromResult(TypedResults.Accepted((string?)null) as IResult);
                              }
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.DoesNotContain(diagnostics, d => d.Id == "SYNE001");
        Assert.Empty(diagnostics.Where(d => d.Id.StartsWith("SYNE", StringComparison.Ordinal)));
    }

    [Fact]
    public void Generate_ForAHandBoundEndpointWithRouteParameters_ReportsNoBindingDiagnostics()
    {
        // Arrange — the message has no property for {slug}, which would be SYNE001 at the high level.
        const string source = """
                              using System.Threading.Tasks;
                              using Microsoft.AspNetCore.Http;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;
                              using UnambitiousFx.Synapse.Endpoints.Binding;

                              namespace TestNs;

                              public sealed record SlugQuery(string Value) : IRequest<string>;

                              [Get("/pages/{slug}")]
                              public sealed class SlugEndpoint : RawEndpoint<SlugQuery, string>
                              {
                                  public override ValueTask<BindResult<SlugQuery>> BindAsync(HttpContext context)
                                  {
                                      context.TryGetRoute("slug", out var slug);
                                      return ValueTask.FromResult(BindResult<SlugQuery>.Success(new SlugQuery(slug!)));
                                  }
                              }
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.DoesNotContain(diagnostics, d => d.Id == "SYNE001");
    }

    // SYNE010 is about whether MapEndpoint<TEndpoint>() can instantiate the class at all, which is
    // just as true of the low level as the high level.
    [Fact]
    public void Generate_ForAGenericFreeFormEndpoint_ReportsSyne010()
    {
        // Arrange
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using Microsoft.AspNetCore.Http;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              [Get("/generic")]
                              public sealed class GenericEndpoint<T> : RawEndpoint
                              {
                                  public override ValueTask<IResult> HandleAsync(HttpContext context, CancellationToken cancellationToken)
                                      => ValueTask.FromResult(TypedResults.NoContent() as IResult);
                              }
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.Contains(diagnostics, d => d.Id == "SYNE010");
    }

    [Fact]
    public void Generate_ForAFreeFormEndpointDeclaringItsRouteTwice_ReportsSyne009()
    {
        // Arrange
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using Microsoft.AspNetCore.Http;
                              using UnambitiousFx.Synapse.Endpoints;
                              using UnambitiousFx.Synapse.Endpoints.Builders;

                              namespace TestNs;

                              [Get("/twice")]
                              public sealed class TwiceEndpoint : RawEndpoint
                              {
                                  public override void Configure(IRawEndpointBuilder builder) => builder.Get("/also-here");

                                  public override ValueTask<IResult> HandleAsync(HttpContext context, CancellationToken cancellationToken)
                                      => ValueTask.FromResult(TypedResults.NoContent() as IResult);
                              }
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.Contains(diagnostics, d => d.Id == "SYNE009");
    }

    // SYNE003/SYNE004/SYNE005 are about dispatch and success mapping, which the two RawEndpoint<…>
    // tiers do exactly as the high level does — they inherit that code — so those rules must still
    // apply to them. The binding rules must not.
    [Fact]
    public void Generate_ForAHandBoundPostReturningAValueWithNoSuccessMapping_ReportsSyne003()
    {
        // Arrange
        const string source = """
                              using System.Threading.Tasks;
                              using Microsoft.AspNetCore.Http;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;
                              using UnambitiousFx.Synapse.Endpoints.Binding;

                              namespace TestNs;

                              public sealed record CreateThingCommand : IRequest<string>;

                              [Post("/things")]
                              public sealed class CreateThingEndpoint : RawEndpoint<CreateThingCommand, string>
                              {
                                  public override ValueTask<BindResult<CreateThingCommand>> BindAsync(HttpContext context)
                                      => ValueTask.FromResult(BindResult<CreateThingCommand>.Success(new CreateThingCommand()));
                              }
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.Contains(diagnostics, d => d.Id == "SYNE003");
    }

    [Fact]
    public void Generate_ForAFreeFormPost_ReportsNoSyne003()
    {
        // Arrange — a free-form endpoint has no success-mapping concept at all: it returns its own
        // result, so there is nothing for the nudge to be about.
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using Microsoft.AspNetCore.Http;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              [Post("/things")]
                              public sealed class CreateThingEndpoint : RawEndpoint
                              {
                                  public override ValueTask<IResult> HandleAsync(HttpContext context, CancellationToken cancellationToken)
                                      => ValueTask.FromResult(TypedResults.Ok("made") as IResult);
                              }
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.DoesNotContain(diagnostics, d => d.Id == "SYNE003");
    }

    [Fact]
    public void Generate_ForAHandBoundEndpointDispatchingAStreamMessage_ReportsSyne005()
    {
        // Arrange
        const string source = """
                              using System.Threading.Tasks;
                              using Microsoft.AspNetCore.Http;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;
                              using UnambitiousFx.Synapse.Endpoints.Binding;

                              namespace TestNs;

                              public sealed record TickQuery : IStreamRequest<int>;

                              [Get("/ticks")]
                              public sealed class TickEndpoint : RawEndpoint<TickQuery, int>
                              {
                                  public override ValueTask<BindResult<TickQuery>> BindAsync(HttpContext context)
                                      => ValueTask.FromResult(BindResult<TickQuery>.Success(new TickQuery()));
                              }
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.Contains(diagnostics, d => d.Id == "SYNE005");
    }

    // Every binding diagnostic must stay silent for a hand-bound endpoint: the generator did not write
    // that binding, so it has no standing to complain about it. Each case here is a shape that WOULD be
    // reported on the equivalent Endpoint<…>.
    [Theory]
    [InlineData("SYNE002")]
    [InlineData("SYNE007")]
    [InlineData("SYNE011")]
    [InlineData("SYNE012")]
    [InlineData("SYNE014")]
    public void Generate_ForAHandBoundEndpoint_ReportsNoBindingDiagnostic(string diagnosticId)
    {
        // Arrange — a message that trips several binding rules at once: two properties claiming the
        // same query key, a [FromBody] on a bodyless verb, a setter-less non-record property, and a
        // property whose type has no TryParse.
        const string source = """
                              using System.Threading.Tasks;
                              using Microsoft.AspNetCore.Http;
                              using Microsoft.AspNetCore.Mvc;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;
                              using UnambitiousFx.Synapse.Endpoints.Binding;

                              namespace TestNs;

                              public sealed class Unparsable;

                              public sealed class MessyQuery : IRequest<string>
                              {
                                  [FromQuery(Name = "same")] public string? First { get; set; }
                                  [FromQuery(Name = "same")] public string? Second { get; set; }
                                  [FromBody] public string? Body { get; set; }
                                  public string ReadOnly { get; } = "";
                                  public Unparsable? Weird { get; set; }
                              }

                              [Get("/messy")]
                              public sealed class MessyEndpoint : RawEndpoint<MessyQuery, string>
                              {
                                  public override ValueTask<BindResult<MessyQuery>> BindAsync(HttpContext context)
                                      => ValueTask.FromResult(BindResult<MessyQuery>.Success(new MessyQuery()));
                              }
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.DoesNotContain(diagnostics, d => d.Id == diagnosticId);
    }

    // The control for the theory above: the identical message on a high-level endpoint DOES get
    // reported, so the suppression is scoped to the endpoint kind and is not a blanket silencing.
    [Fact]
    public void Generate_ForTheSameMessageOnAHighLevelEndpoint_StillReportsTheBindingDiagnostics()
    {
        // Arrange
        const string source = """
                              using Microsoft.AspNetCore.Mvc;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed class MessyQuery : IRequest<string>
                              {
                                  [FromQuery(Name = "same")] public string? First { get; set; }
                                  [FromQuery(Name = "same")] public string? Second { get; set; }
                              }

                              [Get("/messy")]
                              public sealed class MessyEndpoint : Endpoint<MessyQuery, string>;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.Contains(diagnostics, d => d.Id == "SYNE002");
    }
}
