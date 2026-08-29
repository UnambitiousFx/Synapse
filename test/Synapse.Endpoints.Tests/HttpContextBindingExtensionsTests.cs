using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Synapse.Endpoints.Binding;

namespace UnambitiousFx.Synapse.Endpoints.Tests;

public sealed class HttpContextBindingExtensionsTests
{
    [Fact]
    public void TryGetRoute_ReadsTheRawValue()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.RouteValues["taskId"] = "abc";

        // Act
        var found = context.TryGetRoute("taskId", out var value);
        var missing = context.TryGetRoute("nope", out var absent);

        // Assert
        Assert.True(found);
        Assert.Equal("abc", value);
        Assert.False(missing);
        Assert.Null(absent);
    }

    [Fact]
    public void TryGetRoute_Typed_ParsesTheValue()
    {
        // Arrange
        var expected = Guid.NewGuid();
        var context = new DefaultHttpContext();
        context.Request.RouteValues["taskId"] = expected.ToString();
        context.Request.RouteValues["bad"] = "not-a-guid";

        // Act
        var parsed = context.TryGetRoute<Guid>("taskId", out var value);
        var unparsable = context.TryGetRoute<Guid>("bad", out var fallback);

        // Assert
        Assert.True(parsed);
        Assert.Equal(expected, value);
        Assert.False(unparsable);
        Assert.Equal(Guid.Empty, fallback);
    }

    // The reason the typed readers pin a culture at all: a route or query value is a wire format, so
    // "1.5" must mean one and a half on every host. Under a comma-decimal culture the current-culture
    // overload reads it as 15.
    [Fact]
    public void TryGetQuery_Typed_ParsesWithTheInvariantCulture()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?amount=1.5&when=2026-08-25");

        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");

        try
        {
            // Act
            var amountParsed = context.TryGetQuery<decimal>("amount", out var amount);
            var whenParsed = context.TryGetQuery<DateOnly>("when", out var when);

            // Assert
            Assert.True(amountParsed);
            Assert.Equal(1.5m, amount);
            Assert.True(whenParsed);
            Assert.Equal(new DateOnly(2026, 8, 25), when);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void TryGetQueryEnum_AcceptsANameOrANumber()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?day=Friday&num=0&bad=Someday");

        // Act
        var byName = context.TryGetQueryEnum<DayOfWeek>("day", out var named);
        var byNumber = context.TryGetQueryEnum<DayOfWeek>("num", out var numbered);
        var bad = context.TryGetQueryEnum<DayOfWeek>("bad", out _);

        // Assert
        Assert.True(byName);
        Assert.Equal(DayOfWeek.Friday, named);
        Assert.True(byNumber);
        Assert.Equal(DayOfWeek.Sunday, numbered);
        Assert.False(bad);
    }

    [Fact]
    public void Header_ReturnsTheFirstValueOrNull()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Headers["If-None-Match"] = new[] { "\"v1\"", "\"v2\"" };

        // Act & Assert
        Assert.Equal("\"v1\"", context.Header("If-None-Match"));
        Assert.Null(context.Header("X-Absent"));
    }

    [Fact]
    public void QueryValues_ReturnsEveryValueOfARepeatedKey()
    {
        // Arrange — the single-value readers take the first, which is what convention binding needs;
        // a hand-written handler asking for ?tag=a&tag=b needs all of them.
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?tag=a&tag=b&tag=c");

        // Act
        var values = context.QueryValues("tag");
        var absent = context.QueryValues("nope");

        // Assert
        Assert.Equal(3, values.Count);
        Assert.Equal("a,b,c", values.ToString());
        Assert.Equal(0, absent.Count);
        Assert.True(context.TryGetQuery("tag", out var first));
        Assert.Equal("a", first);
    }

    [Fact]
    public async Task BodyAsync_DeserializesTheRequestBody()
    {
        // Arrange
        var context = NewJsonContext("""{"name":"widget"}""", "application/json");

        // Act
        var result = await context.BodyAsync<Payload>(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("widget", result.Value!.Name);
        Assert.Null(result.Errors);
    }

    // The token parameter used to be discarded outright — documented as "unused" while every call site
    // dutifully passed one. It is now linked with RequestAborted, so a caller who wants to bound the
    // read by something of their own can. See docs/known-issues/063.
    [Fact]
    public async Task BodyAsync_HonoursTheCallersCancellationToken()
    {
        // Arrange
        var context = NewJsonContext("""{"name":"ada"}""", "application/json");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act + Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await context.BodyAsync<Payload>(cts.Token));
    }

    [Fact]
    public async Task BodyAsync_ForAnUnusableBody_FailsUnderTheBodyKeyRatherThanThrowing()
    {
        // Arrange — a client mistake must stay a 400, so nothing here escapes as an exception.
        var context = NewJsonContext("{not json", "application/json");

        // Act
        var result = await context.BodyAsync<Payload>(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal([BindingHelpers.BodyField], result.Errors!.Keys);
        Assert.Contains("not valid JSON", result.Errors[BindingHelpers.BodyField][0]);
    }

    [Fact]
    public void Service_ResolvesFromTheRequestScope()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(new Payload { Name = "injected" });
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };

        // Act
        var resolved = context.Service<Payload>();

        // Assert
        Assert.Equal("injected", resolved.Name);
    }

    private static DefaultHttpContext NewJsonContext(string body,
        string? contentType)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, BindingExtensionsJsonContext.Default));

        var bytes = Encoding.UTF8.GetBytes(body);
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        context.Request.ContentType = contentType;
        return context;
    }

    internal sealed class Payload
    {
        public string Name { get; set; } = string.Empty;
    }
}

[JsonSerializable(typeof(HttpContextBindingExtensionsTests.Payload))]
internal sealed partial class BindingExtensionsJsonContext : JsonSerializerContext;
