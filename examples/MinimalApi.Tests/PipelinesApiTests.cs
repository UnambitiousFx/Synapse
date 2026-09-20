using Microsoft.AspNetCore.Mvc.Testing;

namespace UnambitiousFx.Examples.MinimalApi.Tests;

public sealed class PipelinesApiTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PipelinesApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetCreateTaskPipeline_ListsTheCqrsBoundaryBehaviorOutermost()
    {
        // Arrange (Given)
        var client = _factory.CreateClient();

        // Act (When)
        var body = await client.GetStringAsync("/pipelines/create-task", TestContext.Current.CancellationToken);

        // Assert (Then)
        var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.NotEmpty(lines);
        Assert.Contains("CqrsBoundaryEnforcementBehavior", lines[0], StringComparison.Ordinal);
        Assert.Contains(lines, line => line.Contains("MetricsBehavior", StringComparison.Ordinal));
    }
}
