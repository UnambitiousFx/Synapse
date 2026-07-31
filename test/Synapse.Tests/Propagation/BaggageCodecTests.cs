using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Propagation;

namespace UnambitiousFx.Synapse.Tests.Propagation;

public sealed class BaggageCodecTests
{
    [Fact]
    public void Parse_WithARepeatedKey_ChargesTheByteBudgetOnce()
    {
        // Arrange (Given) — a repeated key overwrites rather than adds, so its predecessor's bytes have to come
        // back out of the budget. Charging every repetition exhausted the 8192-byte cap early and dropped the
        // later, valid repetitions (known issue 039).
        var values = Enumerable.Range(0, 5)
                               .Select(i => new string((char)('a' + i), 2_000))
                               .ToArray();
        var header = string.Join(',', values.Select(value => $"k={value}")) + ",tail=kept";

        // Act (When)
        var entries = BaggageCodec.Parse(header, out var dropped);

        // Assert (Then) — last one wins, nothing dropped: five repetitions of 2 KB are one 2 KB entry
        Assert.Equal(0, dropped);
        Assert.Equal(values[^1], entries["k"]);
        Assert.Equal("kept", entries["tail"]);
    }

    [Fact]
    public void Parse_WithARepeatedKeyAtTheEntryCap_AcceptsTheOverwrite()
    {
        // Arrange (Given) — a full collection has room for an overwrite; it adds no entry
        var filled = Enumerable.Range(0, BaggageLimits.MaxEntryCount)
                               .Select(i => $"k{i}=v");
        var header = $"{string.Join(',', filled)},k0=replaced";

        // Act (When)
        var entries = BaggageCodec.Parse(header, out var dropped);

        // Assert (Then)
        Assert.Equal(0, dropped);
        Assert.Equal(BaggageLimits.MaxEntryCount, entries.Count);
        Assert.Equal("replaced", entries["k0"]);
    }

    [Fact]
    public void Parse_WithAnEntryPastTheEntryCap_DropsItAndReportsIt()
    {
        // Arrange (Given) — the cap still holds for genuinely new keys
        var filled = Enumerable.Range(0, BaggageLimits.MaxEntryCount)
                               .Select(i => $"k{i}=v");
        var header = $"{string.Join(',', filled)},one-too-many=v";

        // Act (When)
        var entries = BaggageCodec.Parse(header, out var dropped);

        // Assert (Then)
        Assert.Equal(1, dropped);
        Assert.Equal(BaggageLimits.MaxEntryCount, entries.Count);
        Assert.DoesNotContain("one-too-many", entries.Keys);
    }

    [Fact]
    public void FormatThenParse_WithAValueNeedingEscapes_MeasuresTheWireFormNotTheDecodedOne()
    {
        // Arrange (Given) — every character of this value expands to a three-byte escape, so the decoded length
        // understates the wire length threefold. Measuring the decoded form let baggage pass the check and still
        // exceed the W3C limit on the wire, where an intermediary is free to truncate it (known issue 039).
        var value = new string(',', 3_000);

        // Act (When)
        var decodedBytes = System.Text.Encoding.UTF8.GetByteCount(value);
        var measured = BaggageLimits.MeasureEntry("k", value);
        var wireLength = BaggageCodec.Format(new Dictionary<string, string> { ["k"] = value })!.Length;

        // Assert (Then) — the measurement matches what Format actually emits, plus the "," separator
        Assert.Equal(3_000, decodedBytes);
        Assert.Equal(9_000, BaggageLimits.MeasureEncodedValue(value));
        Assert.Equal(wireLength + 1, measured);
        Assert.True(measured > BaggageLimits.MaxTotalBytes);
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("Acme, Inc")]
    [InlineData("a=b;c")]
    [InlineData("100% café")]
    [InlineData("tilde~dash-dot.under_")]
    public void MeasureEncodedValue_MatchesWhatEscapingProduces(string value)
    {
        // Arrange (Given) / Act (When) — the measurement is a count rather than an encode, so it has to agree
        // with the encoder for every character class
        var measured = BaggageLimits.MeasureEncodedValue(value);

        // Assert (Then)
        Assert.Equal(Uri.EscapeDataString(value)
                        .Length, measured);
    }

    [Fact]
    public void Parse_WithAnUnparseableSegment_DropsOnlyThatSegment()
    {
        // Arrange (Given)
        const string header = "good=1,no-separator,=novalue,also.good=2";

        // Act (When)
        var entries = BaggageCodec.Parse(header, out var dropped);

        // Assert (Then)
        Assert.Equal(2, dropped);
        Assert.Equal("1", entries["good"]);
        Assert.Equal("2", entries["also.good"]);
    }
}
