using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
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

    [Fact]
    public async Task ReadJsonBodyAsync_WhenBodyIsMalformedJson_ReturnsAFailureMentioningNotValidJson()
    {
        // Arrange
        var context = CreateContext("{not json");

        // Act
        var result = await BindingHelpers.ReadJsonBodyAsync<BindingHelpersTestPayload>(context);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("not valid JSON", result.Error);
    }

    [Fact]
    public async Task ReadJsonBodyAsync_WhenBodyIsNull_ReturnsAFailureMentioningRequired()
    {
        // Arrange — the literal JSON `null` deserializes successfully to a null value, which is the
        // "empty body" case this method must turn into a failure rather than a null message.
        var context = CreateContext("null");

        // Act
        var result = await BindingHelpers.ReadJsonBodyAsync<BindingHelpersTestPayload>(context);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("required", result.Error);
    }

    private static DefaultHttpContext CreateContext(string body)
    {
        var services = new ServiceCollection();
        services.ConfigureHttpJsonOptions(o =>
            o.SerializerOptions.TypeInfoResolverChain.Insert(0, BindingHelpersTestJsonContext.Default));

        var bytes = Encoding.UTF8.GetBytes(body);
        return new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            Request =
            {
                Body = new MemoryStream(bytes),
                ContentLength = bytes.Length,
                ContentType = "application/json"
            }
        };
    }
}

internal sealed record BindingHelpersTestPayload(string Name);

[JsonSerializable(typeof(BindingHelpersTestPayload))]
internal sealed partial class BindingHelpersTestJsonContext : JsonSerializerContext;
