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

    [Fact]
    public async Task ReadJsonBodyAsync_WhenBodyHasNoContentType_ReturnsAFailureRatherThanThrowing()
    {
        // Arrange — a body with no Content-Type header at all. This is the one malformed-request shape
        // the Accepts-driven consumes matcher policy lets through (an absent content type matches any
        // endpoint), and ReadFromJsonAsync throws InvalidOperationException for it rather than
        // JsonException — so before the content-type guard it escaped the catch, producing a 500 and
        // an "An unhandled exception was thrown" log line per request instead of a 400.
        var context = CreateContext("""{"name":"x"}""", contentType: null);

        // Act
        var result = await BindingHelpers.ReadJsonBodyAsync<BindingHelpersTestPayload>(context);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("JSON", result.Error);
    }

    [Fact]
    public async Task ReadJsonBodyAsync_WhenContentTypeIsNotJson_ReturnsAFailureNamingIt()
    {
        // Arrange — normally rejected with 415 during routing, but the helper must not depend on that:
        // an endpoint that does not declare Accepts (or a direct call) still reaches here.
        var context = CreateContext("""{"name":"x"}""", contentType: "text/plain");

        // Act
        var result = await BindingHelpers.ReadJsonBodyAsync<BindingHelpersTestPayload>(context);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("text/plain", result.Error);
    }

    [Theory]
    [InlineData("application/json")]
    [InlineData("application/json; charset=utf-8")]
    [InlineData("application/problem+json")]
    public async Task ReadJsonBodyAsync_ForAJsonContentType_StillReadsTheBody(string contentType)
    {
        // Arrange — the guard must not reject the JSON content types that legitimately arrive: a
        // charset parameter and the "+json" structured suffix both count as JSON.
        var context = CreateContext("""{"name":"kept"}""", contentType: contentType);

        // Act
        var result = await BindingHelpers.ReadJsonBodyAsync<BindingHelpersTestPayload>(context);

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("kept", result.Value!.Name);
    }

    private static DefaultHttpContext CreateContext(string body,
        bool omitContentLength = false,
        string? contentType = "application/json")
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
                ContentType = contentType
            }
        };
    }
}

internal sealed record BindingHelpersTestPayload(string Name);

[JsonSerializable(typeof(BindingHelpersTestPayload))]
internal sealed partial class BindingHelpersTestJsonContext : JsonSerializerContext;
