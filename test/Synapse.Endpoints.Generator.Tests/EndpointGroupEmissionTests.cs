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

                              public sealed record GetThingQuery : IRequest<string>;

                              [Get("/things/{id}")]
                              public sealed class GetThingEndpoint : Endpoint<GetThingQuery, string>;
                              """;

        // Act
        var generated = GeneratorHarness.GetFile(source, "EndpointGroup.g.cs");

        // Assert
        Assert.Contains("public sealed class EndpointGroup : global::UnambitiousFx.Synapse.Endpoints.IEndpointGroup", generated);
        Assert.Contains("endpoints.MapEndpoint<global::TestNs.GetThingEndpoint>();", generated);
    }

    [Fact]
    public void Generate_ForOneEndpoint_EmitsMetadataRegistration()
    {
        // Arrange
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record GetThingQuery : IRequest<string>;

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
        var generated = GeneratorHarness.TryGetFile(source, "EndpointGroup.g.cs");

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

        // Act & Assert
        GeneratorHarness.AssertGeneratedCompiles(source);
    }
}
