using System.Text.Json.Nodes;
using JetBrains.Annotations;
using ModelContextProtocol.Protocol;

namespace UnambitiousFx.Synapse.ModelContextProtocol.Tests;

[TestSubject(typeof(McpMetaPropagationCarrier))]
public sealed class McpMetaPropagationCarrierTests
{
    [Fact]
    public void TryGetValue_WithStringEntry_ReturnsIt()
    {
        // Arrange (Given)
        var parameters = new CallToolRequestParams
        {
            Name = "get_loan",
            Meta = new JsonObject { ["traceparent"] = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01" }
        };
        var sut = new McpMetaPropagationCarrier(parameters);

        // Act (When)
        var found = sut.TryGetValue("traceparent", out var value);

        // Assert (Then)
        Assert.True(found);
        Assert.Equal("00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01", value);
    }

    [Fact]
    public void TryGetValue_WithoutMeta_ReturnsFalse()
    {
        // Arrange (Given)
        var sut = new McpMetaPropagationCarrier(new CallToolRequestParams { Name = "get_loan" });

        // Act (When)
        var found = sut.TryGetValue("traceparent", out var value);

        // Assert (Then)
        Assert.False(found);
        Assert.Null(value);
    }

    [Fact]
    public void TryGetValue_WithNonStringEntry_ReturnsFalse()
    {
        // Arrange (Given)
        var parameters = new CallToolRequestParams
        {
            Name = "get_loan",
            Meta = new JsonObject { ["baggage"] = new JsonArray("a", "b"), ["tracestate"] = 3 }
        };
        var sut = new McpMetaPropagationCarrier(parameters);

        // Act (When)
        var baggage = sut.TryGetValue("baggage", out _);
        var traceState = sut.TryGetValue("tracestate", out _);

        // Assert (Then)
        Assert.False(baggage);
        Assert.False(traceState);
    }

    [Fact]
    public void Set_WithoutMeta_CreatesItAndWritesTheValue()
    {
        // Arrange (Given)
        var parameters = new CallToolRequestParams { Name = "get_loan" };
        var sut = new McpMetaPropagationCarrier(parameters);

        // Act (When)
        sut.Set("baggage", "tenant=acme");
        sut.Set("baggage", "tenant=globex");

        // Assert (Then)
        Assert.NotNull(parameters.Meta);
        Assert.Equal("tenant=globex", parameters.Meta["baggage"]!.GetValue<string>());
    }

    [Fact]
    public void Constructor_WithNullParameters_Throws()
    {
        // Act (When)
        var exception = Record.Exception(() => new McpMetaPropagationCarrier(null!));

        // Assert (Then)
        Assert.IsType<ArgumentNullException>(exception);
    }
}
