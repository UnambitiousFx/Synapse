using Microsoft.AspNetCore.Mvc.Testing;
using UnambitiousFx.Synapse;

namespace UnambitiousFx.Examples.MinimalApi.Tests;

public sealed class ValidationTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ValidationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public void ValidateSynapse_WithExampleConfiguration_ReportsNoErrors()
    {
        // Arrange (Given)
        var provider = _factory.Services;

        // Act (When)
        var report = provider.ValidateSynapse();

        // Assert (Then)
        Assert.Empty(report.Issues);
        Assert.True(report.IsValid, report.ToString());
    }
}
