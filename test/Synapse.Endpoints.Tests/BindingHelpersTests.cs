using Microsoft.AspNetCore.Http;
using UnambitiousFx.Synapse.Endpoints.Binding;

namespace UnambitiousFx.Synapse.Endpoints.Tests;

public sealed class BindingHelpersTests
{
    [Fact]
    public void TryGetRoute_WhenValuePresent_ReturnsTrueAndTheValue()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.RouteValues["taskId"] = "42";

        // Act
        var found = BindingHelpers.TryGetRoute(context, "taskId", out var value);

        // Assert
        Assert.True(found);
        Assert.Equal("42", value);
    }

    [Fact]
    public void TryGetQuery_WhenKeyMissing_ReturnsFalse()
    {
        // Arrange
        var context = new DefaultHttpContext();

        // Act
        var found = BindingHelpers.TryGetQuery(context, "page", out var value);

        // Assert
        Assert.False(found);
        Assert.Null(value);
    }

    [Fact]
    public void TryGetHeader_WhenHeaderPresent_ReturnsTheFirstValue()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Headers["If-Match"] = "\"abc\"";

        // Act
        var found = BindingHelpers.TryGetHeader(context, "If-Match", out var value);

        // Assert
        Assert.True(found);
        Assert.Equal("\"abc\"", value);
    }
}
