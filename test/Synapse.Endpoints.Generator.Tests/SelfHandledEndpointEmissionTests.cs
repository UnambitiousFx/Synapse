namespace UnambitiousFx.Synapse.Endpoints.Generator.Tests;

public sealed class SelfHandledEndpointEmissionTests
{
    private const string ValueEndpoint = """
                                         using System.Threading;
                                         using System.Threading.Tasks;
                                         using Microsoft.AspNetCore.Http;
                                         using UnambitiousFx.Functional;
                                         using UnambitiousFx.Synapse.Endpoints;

                                         namespace TestNs;

                                         public sealed record ProbeQuery
                                         {
                                             public string Probe { get; init; } = "";
                                         }

                                         public sealed record ProbeDto(string Probe, bool Healthy);

                                         [Get("/health/{probe}")]
                                         public sealed class ProbeEndpoint : SelfHandledEndpoint<ProbeQuery, ProbeDto>
                                         {
                                             public override ValueTask<Result<ProbeDto>> ExecuteAsync(ProbeQuery request,
                                                 HttpContext context,
                                                 CancellationToken cancellationToken)
                                             {
                                                 return ValueTask.FromResult(Result.Success(new ProbeDto(request.Probe, true)));
                                             }
                                         }
                                         """;

    private const string VoidEndpoint = """
                                        using System.Threading;
                                        using System.Threading.Tasks;
                                        using Microsoft.AspNetCore.Http;
                                        using UnambitiousFx.Functional;
                                        using UnambitiousFx.Synapse.Endpoints;

                                        namespace TestNs;

                                        public sealed record PurgeRequest
                                        {
                                            public string Key { get; init; } = "";
                                        }

                                        [Delete("/cache/{key}")]
                                        public sealed class PurgeEndpoint : SelfHandledEndpoint<PurgeRequest>
                                        {
                                            public override ValueTask<Result> ExecuteAsync(PurgeRequest request,
                                                HttpContext context,
                                                CancellationToken cancellationToken)
                                            {
                                                return ValueTask.FromResult(Result.Success());
                                            }
                                        }
                                        """;

    [Fact]
    public void Generate_ForSelfHandledEndpoint_EmitsABinderForARequestThatIsNotAMessage()
    {
        // Act
        var generated = GeneratorHarness.GetFile(ValueEndpoint, "SynapseEndpointBinders.g.cs");

        // Assert
        Assert.Contains("global::TestNs.ProbeQuery", generated);
        Assert.Contains("TryGetRoute(context, \"probe\", out var", generated);
        GeneratorHarness.AssertGeneratedCompiles(ValueEndpoint);
    }

    [Fact]
    public void Generate_ForSelfHandledVoidEndpoint_EmitsABinderForARequestThatIsNotAMessage()
    {
        // Act
        var generated = GeneratorHarness.GetFile(VoidEndpoint, "SynapseEndpointBinders.g.cs");

        // Assert
        Assert.Contains("global::TestNs.PurgeRequest", generated);
        Assert.Contains("TryGetRoute(context, \"key\", out var", generated);
        GeneratorHarness.AssertGeneratedCompiles(VoidEndpoint);
    }

    [Fact]
    public void Generate_ForSelfHandledEndpoint_MapsItAlongsideTheDispatchingTiers()
    {
        // Act
        var generated = GeneratorHarness.GetFile(ValueEndpoint, "SynapseEndpointGroup.g.cs");

        // Assert
        Assert.Contains("MapEndpoint<global::TestNs.ProbeEndpoint>()", generated);
    }
}
