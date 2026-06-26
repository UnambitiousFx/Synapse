using JetBrains.Annotations;
using UnambitiousFx.Synapse.Generator;

namespace UnambitiousFx.Synapse.Generator.Tests;

[TestSubject(typeof(EquatableArray<>))]
public sealed class EquatableArrayTests
{
    // ── Equality ─────────────────────────────────────────────────────────────

    [Fact]
    public void Equals_SameElements_ReturnsTrue()
    {
        // Arrange (Given)
        var a = new EquatableArray<int>([1, 2, 3]);
        var b = new EquatableArray<int>([1, 2, 3]);

        // Assert (Then)
        Assert.Equal(a, b);
        Assert.True(a.Equals(b));
    }

    [Fact]
    public void Equals_DifferentElements_ReturnsFalse()
    {
        // Arrange (Given)
        var a = new EquatableArray<int>([1, 2, 3]);
        var b = new EquatableArray<int>([1, 2, 4]);

        // Assert (Then)
        Assert.NotEqual(a, b);
        Assert.False(a.Equals(b));
    }

    [Fact]
    public void Equals_DifferentOrder_ReturnsFalse()
    {
        // Arrange (Given)
        var a = new EquatableArray<int>([1, 2, 3]);
        var b = new EquatableArray<int>([3, 2, 1]);

        // Assert (Then)
        Assert.False(a.Equals(b));
    }

    [Fact]
    public void Equals_DefaultStructAndEmptyArray_ReturnsTrue()
    {
        // Arrange (Given) — both wrap empty / null arrays → empty spans → SequenceEqual
        var defaultStruct = default(EquatableArray<int>);
        var emptyArray = new EquatableArray<int>([]);

        // Assert (Then)
        Assert.True(defaultStruct.Equals(emptyArray));
        Assert.True(emptyArray.Equals(defaultStruct));
    }

    [Fact]
    public void Equals_Object_NonEquatableArray_ReturnsFalse()
    {
        // Arrange (Given)
        var a = new EquatableArray<int>([1, 2]);

        // Assert (Then)
        Assert.False(a.Equals("not-an-array"));
        Assert.False(a.Equals(null));
        Assert.False(a.Equals(42));
    }

    // ── GetHashCode ───────────────────────────────────────────────────────────

    [Fact]
    public void GetHashCode_NullArray_ReturnsZero()
    {
        // Arrange (Given)
        var defaultStruct = default(EquatableArray<int>);

        // Assert (Then)
        Assert.Equal(0, defaultStruct.GetHashCode());
    }

    [Fact]
    public void GetHashCode_EqualArrays_ReturnSameHash()
    {
        // Arrange (Given)
        var a = new EquatableArray<int>([1, 2, 3]);
        var b = new EquatableArray<int>([1, 2, 3]);

        // Assert (Then)
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void GetHashCode_DifferentArrays_ReturnDifferentHash()
    {
        // Arrange (Given)
        var a = new EquatableArray<int>([1, 2, 3]);
        var b = new EquatableArray<int>([4, 5, 6]);

        // Assert (Then)
        Assert.NotEqual(a.GetHashCode(), b.GetHashCode());
    }

    // ── Count ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Count_NullArray_ReturnsZero()
    {
        // Arrange (Given)
        var defaultStruct = default(EquatableArray<int>);

        // Assert (Then)
        Assert.Equal(0, defaultStruct.Count);
    }

    [Fact]
    public void Count_ReturnsLength()
    {
        // Arrange (Given)
        var arr = new EquatableArray<int>([10, 20, 30]);

        // Assert (Then)
        Assert.Equal(3, arr.Count);
    }

    // ── Indexer ───────────────────────────────────────────────────────────────

    [Fact]
    public void Indexer_ReturnsElement()
    {
        // Arrange (Given)
        var arr = new EquatableArray<int>([7, 8, 9]);

        // Assert (Then)
        Assert.Equal(7, arr[0]);
        Assert.Equal(8, arr[1]);
        Assert.Equal(9, arr[2]);
    }

    // ── Enumeration ───────────────────────────────────────────────────────────

    [Fact]
    public void Enumeration_NullArray_YieldsEmpty()
    {
        // Arrange (Given)
        var defaultStruct = default(EquatableArray<int>);

        // Act (When)
        var items = defaultStruct.ToList();

        // Assert (Then)
        Assert.Empty(items);
    }

    [Fact]
    public void Enumeration_YieldsAllElements()
    {
        // Arrange (Given)
        var arr = new EquatableArray<int>([1, 2, 3]);

        // Act (When)
        var items = arr.ToList();

        // Assert (Then)
        Assert.Equal([1, 2, 3], items);
    }

    // ── From ──────────────────────────────────────────────────────────────────

    [Fact]
    public void From_CopiesSource()
    {
        // Arrange (Given)
        var source = new List<int> { 1, 2, 3 };

        // Act (When)
        var arr = EquatableArray<int>.From(source);

        // Assert (Then)
        Assert.Equal(new EquatableArray<int>([1, 2, 3]), arr);
        Assert.Equal(3, arr.Count);
    }

    // ── AsSpan ────────────────────────────────────────────────────────────────

    [Fact]
    public void AsSpan_NullArray_ReturnsEmptySpan()
    {
        // Arrange (Given)
        var defaultStruct = default(EquatableArray<int>);

        // Act (When)
        var span = defaultStruct.AsSpan();

        // Assert (Then)
        Assert.Equal(0, span.Length);
    }

    [Fact]
    public void AsSpan_ReturnsSpanOverElements()
    {
        // Arrange (Given)
        var arr = new EquatableArray<int>([5, 6, 7]);

        // Act (When)
        var span = arr.AsSpan();

        // Assert (Then)
        Assert.Equal(3, span.Length);
        Assert.Equal(5, span[0]);
    }
}
