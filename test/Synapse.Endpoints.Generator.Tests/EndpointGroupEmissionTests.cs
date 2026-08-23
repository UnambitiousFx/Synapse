using Microsoft.CodeAnalysis.CSharp;

namespace UnambitiousFx.Synapse.Endpoints.Generator.Tests;

public sealed class EndpointGroupEmissionTests
{
    [Fact]
    public void Generate_ForOneEndpoint_EmitsMapEndpointCall()
    {
        // Arrange
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record GetThingQuery : IRequest<string>
                              {
                                  public string Id { get; init; } = "";
                              }

                              [Get("/things/{id}")]
                              public sealed class GetThingEndpoint : Endpoint<GetThingQuery, string>;
                              """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointGroup.g.cs");

        // Assert
        Assert.Contains("public sealed class SynapseEndpointGroup : global::UnambitiousFx.Synapse.Endpoints.IEndpointGroup", generated);
        Assert.Contains("endpoints.MapEndpoint<global::TestNs.GetThingEndpoint>();", generated);
    }

    [Fact]
    public void Generate_WithRootNamespaceDisjointFromSynapseEndpoints_EmittedGroupCompiles()
    {
        // Arrange — build_property.RootNamespace set to a namespace that is NOT nested under
        // UnambitiousFx.Synapse.Endpoints, matching a real consumer rather than the coincidental
        // fallback ("UnambitiousFx.Synapse.Endpoints.Generated") every other test in this suite
        // exercises without ever setting the property at all. That fallback happens to be a child
        // namespace of UnambitiousFx.Synapse.Endpoints, which let an emitted, unqualified
        // `endpoints.MapEndpoint<T>()` extension-method call resolve by namespace-nesting
        // coincidence even with no `using` in the generated file — this test's whole point is to
        // not have that coincidence available.
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace Acme.Api;

                              public sealed record GetThingQuery : IRequest<string>
                              {
                                  public string Id { get; init; } = "";
                              }

                              [Get("/things/{id}")]
                              public sealed class GetThingEndpoint : Endpoint<GetThingQuery, string>;
                              """;

        // Act & Assert — compiles the emitted SynapseEndpointGroup.g.cs (and its siblings) with
        // that disjoint root namespace. Reverting the `using UnambitiousFx.Synapse.Endpoints;` fix
        // in EndpointGroupEmitter.EmitGroup makes this fail with CS1061 while every other test in
        // this file keeps passing — that gap is exactly what this test exists to close.
        GeneratorHarness.AssertGeneratedCompilesWithRootNamespace(source, "Acme.Api");
    }

    [Fact]
    public void Generate_ForOneEndpoint_EmitsMetadataRegistration()
    {
        // Arrange
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record GetThingQuery : IRequest<string>
                              {
                                  public string Id { get; init; } = "";
                              }

                              [Get("/things/{id}")]
                              public sealed class GetThingEndpoint : Endpoint<GetThingQuery, string>;
                              """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointRegistrations.g.cs");

        // Assert
        Assert.Contains("[global::System.Runtime.CompilerServices.ModuleInitializer]", generated);
        Assert.Contains(
            "global::UnambitiousFx.Synapse.Endpoints.Binding.EndpointRegistry.RegisterMetadata<global::TestNs.GetThingEndpoint>(",
            generated);
        Assert.Contains("new global::UnambitiousFx.Synapse.Endpoints.EndpointMetadata(new[] { \"GET\" }, \"/things/{id}\")", generated);
    }

    [Fact]
    public void Generate_WhenNoEndpoints_EmitsNothing()
    {
        // Arrange
        const string source = "namespace TestNs; public sealed class NotAnEndpoint;";

        // Act
        var generated = GeneratorHarness.TryGetFile(source, "SynapseEndpointGroup.g.cs");

        // Assert
        Assert.Null(generated);
    }

    [Fact]
    public void Generate_ForEveryEndpointShape_Compiles()
    {
        // Arrange
        const string source = """
                              using System.Collections.Generic;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record VoidCommand : IRequest;
                              public sealed record ValueQuery : IRequest<int>;
                              public sealed record TickQuery : IStreamRequest<int>;
                              public sealed record HttpBody(string Name);
                              public sealed record MappedCommand(string Name) : IRequest<int>;
                              public sealed record HttpOut(string Id);

                              [Post("/void")]   public sealed class VoidEndpoint : Endpoint<VoidCommand>;
                              [Get("/value")]   public sealed class ValueEndpoint : Endpoint<ValueQuery, int>;
                              [Get("/ticks")]   public sealed class TickEndpoint : StreamEndpoint<TickQuery, int>;
                              [Post("/mapped")]
                              public sealed class MappedThingEndpoint : MappedEndpoint<HttpBody, MappedCommand, int, HttpOut>
                              {
                                  public override MappedCommand ToRequest(HttpBody request) => new(request.Name);
                                  public override HttpOut ToResponse(int response) => new(response.ToString());
                              }
                              """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointGroup.g.cs");

        // Assert — one MapEndpoint call per shape, so a base-type match that silently fails for one
        // shape (e.g. MappedEndpoint`4 or StreamEndpoint`2) can't hide behind the other three still
        // emitting cleanly.
        Assert.Contains("endpoints.MapEndpoint<global::TestNs.VoidEndpoint>();", generated);
        Assert.Contains("endpoints.MapEndpoint<global::TestNs.ValueEndpoint>();", generated);
        Assert.Contains("endpoints.MapEndpoint<global::TestNs.TickEndpoint>();", generated);
        Assert.Contains("endpoints.MapEndpoint<global::TestNs.MappedThingEndpoint>();", generated);

        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    [Fact]
    public void Generate_ForRouteWithQuoteAndBackslash_EscapesLiteral()
    {
        // Arrange — a route containing a double quote and a backslash, the two characters that break
        // a naively-interpolated C# string literal (or, worse, change what the generated code means).
        const string route = "/a\"b\\c";
        var routeLiteral = SymbolDisplay.FormatLiteral(route, quote: true);
        var naiveUnescapedLiteral = "\"" + route + "\"";

        var source = $"""
                      using UnambitiousFx.Synapse.Abstractions;
                      using UnambitiousFx.Synapse.Endpoints;

                      namespace TestNs;

                      public sealed record WeirdQuery : IRequest<string>;

                      [Get({routeLiteral})]
                      public sealed class WeirdRouteEndpoint : Endpoint<WeirdQuery, string>;
                      """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointRegistrations.g.cs");

        // Assert — the route is rendered through Roslyn's own escaper, not hand-rolled interpolation.
        Assert.Contains(routeLiteral, generated);
        Assert.DoesNotContain(naiveUnescapedLiteral, generated);

        // And the escaped literal round-trips as valid, compilable C#.
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    [Fact]
    public void Generate_ForEndpointInGroup_EmitsGroupFactory()
    {
        // Arrange
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;
                              using UnambitiousFx.Synapse.Endpoints.Builders;

                              namespace TestNs;

                              public sealed record GroupedQuery : IRequest<string>
                              {
                                  public string Id { get; init; } = "";
                              }

                              public sealed class MyGroup : EndpointGroup
                              {
                                  public override void Configure(IEndpointGroupBuilder builder)
                                  {
                                  }
                              }

                              [Get("/things/{id}")]
                              [InGroup<MyGroup>]
                              public sealed class GroupedEndpoint : Endpoint<GroupedQuery, string>;
                              """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "SynapseEndpointRegistrations.g.cs");

        // Assert
        Assert.Contains("typeof(global::TestNs.MyGroup)", generated);
        Assert.Contains("static () => new global::TestNs.MyGroup()", generated);

        // And the four-argument EndpointMetadata overload plus the Func<MyGroup> -> Func<EndpointGroup>
        // covariant conversion actually compile.
        GeneratorHarness.AssertGeneratedCompiles(source);
    }
}
