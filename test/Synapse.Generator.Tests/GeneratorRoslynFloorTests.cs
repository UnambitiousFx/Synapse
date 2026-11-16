using UnambitiousFx.Synapse.Generator;

namespace UnambitiousFx.Synapse.Generator.Tests;

/// <summary>
///     The generator runs inside the consumer's compiler, which refuses an analyzer built against a newer Roslyn than
///     itself (CS9057). It must stay on the Roslyn of the oldest supported SDK, .NET SDK 8.0.100 (Roslyn 4.8).
/// </summary>
public class GeneratorRoslynFloorTests
{
    private static readonly Version OldestSupportedRoslyn = new(4, 8, 0, 0);

    [Theory]
    [InlineData("Microsoft.CodeAnalysis")]
    [InlineData("Microsoft.CodeAnalysis.CSharp")]
    public void Generator_ReferencedRoslynAssembly_IsNoNewerThanTheOldestSupportedSdk(string assembly)
    {
        // Arrange
        var references = typeof(SynapseGenerator).Assembly.GetReferencedAssemblies();

        // Act
        var roslyn = references.Single(reference => reference.Name == assembly);

        // Assert
        Assert.True(roslyn.Version <= OldestSupportedRoslyn,
            $"The generator references {assembly} {roslyn.Version}, newer than {OldestSupportedRoslyn} (.NET SDK 8.0.100): " +
            "consumers on an older SDK fail with CS9057. Keep the VersionOverride in Synapse.Generator.csproj.");
    }
}
