using JetBrains.Annotations;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Tests.Pipelines;

[TestSubject(typeof(PipelineDescription))]
public sealed class PipelineDescriptionTests
{
    [Fact]
    public void Equals_WithIndependentlyBuiltEqualContents_IsEqualWithEqualHashCodes()
    {
        // Arrange (Given)
        var left = Build(typeof(string), typeof(int));
        var right = Build(typeof(string), typeof(int));

        // Act (When)
        var equal = left.Equals(right);

        // Assert (Then)
        Assert.True(equal);
        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void Equals_WithDifferentBehaviorOrder_IsNotEqual()
    {
        // Arrange (Given)
        var left = Build(typeof(string), typeof(int));
        var right = Build(typeof(int), typeof(string));

        // Act (When) / Assert (Then)
        Assert.NotEqual(left, right);
    }

    [Fact]
    public void Equals_WithDifferentHandlers_IsNotEqual()
    {
        // Arrange (Given)
        var left = new PipelineDescription([typeof(string)], []);
        var right = new PipelineDescription([typeof(int)], []);

        // Act (When) / Assert (Then)
        Assert.NotEqual(left, right);
    }

    [Fact]
    public void Equals_WithNull_IsFalse()
    {
        // Arrange (Given)
        var description = Build(typeof(string), typeof(int));

        // Act (When) / Assert (Then)
        Assert.False(description.Equals(null));
    }

    [Fact]
    public void ToString_ListsHandlersAndBehaviorsWithTheirOrder()
    {
        // Arrange (Given)
        var description = new PipelineDescription(
            [typeof(DescribedHandler)],
            [new BehaviorDescription(typeof(DescribedBehavior), 5)]);

        // Act (When)
        var text = description.ToString();

        // Assert (Then)
        Assert.Contains("5 DescribedBehavior", text, StringComparison.Ordinal);
        Assert.Contains("DescribedHandler", text, StringComparison.Ordinal);
    }

    private static PipelineDescription Build(params Type[] behaviors)
    {
        return new PipelineDescription(
            [typeof(DescribedHandler)],
            behaviors.Select((type, index) => new BehaviorDescription(type, (uint)index)).ToArray());
    }

    private sealed class DescribedHandler;

    private sealed class DescribedBehavior;
}
