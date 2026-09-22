using UnambitiousFx.Synapse.Generator;

namespace UnambitiousFx.Synapse.Generator.Tests;

public sealed class RegisterGroupNamingTests
{
    [Fact]
    public void Resolve_WithNoCustomTarget_ReturnsDefaultNameInRootNamespace()
    {
        // Arrange (Given)
        // Act (When)
        var (@namespace, className) = RegisterGroupNaming.Resolve("MyApp.Host", null);

        // Assert (Then)
        Assert.Equal("MyApp.Host", @namespace);
        Assert.Equal("RegisterGroup", className);
    }

    [Fact]
    public void Resolve_WithCustomTarget_ReturnsTheCustomNameIgnoringRootNamespace()
    {
        // Arrange (Given)
        // Act (When)
        var (@namespace, className) = RegisterGroupNaming.Resolve("MyApp.Host",
            ("MyApp.Host.Wiring", "MyGroup"));

        // Assert (Then)
        Assert.Equal("MyApp.Host.Wiring", @namespace);
        Assert.Equal("MyGroup", className);
    }
}
