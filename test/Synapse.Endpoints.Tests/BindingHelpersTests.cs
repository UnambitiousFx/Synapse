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
        // Arrange — the literal JSON `null` deserializes successfully to a null value, which is a
        // different (legitimate) case from a genuinely empty body: this one reaches the `value is
        // null` check below, not the Content-Length short-circuit.
        var context = CreateContext("null");

        // Act
        var result = await BindingHelpers.ReadJsonBodyAsync<BindingHelpersTestPayload>(context);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("required", result.Error);
    }

    [Fact]
    public async Task ReadJsonBodyAsync_WhenBodyIsGenuinelyEmpty_ReturnsAFailureMentioningRequired()
    {
        // Arrange — a real 0-byte body with Content-Length: 0, e.g. a POST with no payload at all.
        // System.Text.Json does not deserialize an empty stream to null; it throws JsonException
        // ("input does not contain any JSON tokens"), which is why this must be checked explicitly
        // via Content-Length before ever calling ReadFromJsonAsync — verified empirically that,
        // without that check, this exact scenario produces "not valid JSON" instead.
        var context = CreateContext("");

        // Act
        var result = await BindingHelpers.ReadJsonBodyAsync<BindingHelpersTestPayload>(context);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("required", result.Error);
    }

    [Fact]
    public async Task ReadJsonBodyAsync_WhenBodyIsEmptyWithNoContentLengthHeader_StillFailsSensibly()
    {
        // Arrange — a chunked-transfer request has no Content-Length header at all, so the cheap
        // `ContentLength == 0` check above cannot see this case; documenting the current fallback
        // behaviour (the JsonException branch) rather than leaving it unverified. This still reports
        // failure, just with the "not valid JSON" wording instead of "required" — a real fix would
        // need to peek the stream, which is out of proportion to how this combination arises in
        // practice (most HTTP clients, including Kestrel's own request pipeline paths, set
        // Content-Length for a non-chunked empty body).
        var context = CreateContext("", omitContentLength: true);

        // Act
        var result = await BindingHelpers.ReadJsonBodyAsync<BindingHelpersTestPayload>(context);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
    }

    private static DefaultHttpContext CreateContext(string body,
        bool omitContentLength = false)
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
                ContentLength = omitContentLength ? null : bytes.Length,
                ContentType = "application/json"
            }
        };
    }
}

internal sealed record BindingHelpersTestPayload(string Name);

[JsonSerializable(typeof(BindingHelpersTestPayload))]
internal sealed partial class BindingHelpersTestJsonContext : JsonSerializerContext;
